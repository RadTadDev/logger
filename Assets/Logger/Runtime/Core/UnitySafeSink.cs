using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace RadTadDev.Logger
{
    /// <summary>
    /// A base sink type that is safe to call the Unity API from. These sinks will
    /// collect messages from other threads and queue handling them as work to perform
    /// on the main thread.
    /// </summary>
    public abstract class UnitySafeSink : ThreadSafeSink
    {
        private readonly struct Entry
        {
            /// <summary>
            /// The message to log.
            /// </summary>
            public readonly string Message;

            /// <summary>
            /// The level to log the message at.
            /// </summary>
            public readonly LogLevel Level;

            /// <summary>
            /// Creates a new entry.
            /// </summary>
            /// <param name="message">The message to log.</param>
            /// <param name="level">The level to log at.</param>
            public Entry(string message, LogLevel level)
            {
                Message = message;
                Level = level;
            }
        }

        private static SynchronizationContext _mainContext;
        private static int _mainThreadId = -1;

        // Stateless, so it's fine that it survives with domain reload off.
        private static readonly SendOrPostCallback DrainCallback = state => ((UnitySafeSink)state).DrainFrame();
        private static readonly SendOrPostCallback CloseCallback = state => ((UnitySafeSink)state).FinishClose();

        private readonly int _drainPerFrameLimit;
        private readonly int _queueLimit;

        private const int EmptyDrain = 0;
        private const int PendingDrain = 1;

        private int _drainScheduled;

        // Main thread only. Spent by every drain, refilled once per frame by the posted drain.
        private int _drainBudget;
        private bool _throttled;

        private int _queued;
        private int _dropped;

        private readonly ConcurrentQueue<Entry> _queue = new();
        private readonly object _pumpLock = new();
        private bool _closed;

        private static bool OnMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        /// <summary>
        /// Creates a new Unity log sink.
        /// </summary>
        /// <param name="drainPerFrameLimit">Maximum number of logs to print in a single frame.</param>
        /// <param name="queueLimit">The maximum number of logs to queue at any given time.</param>
        protected UnitySafeSink(int drainPerFrameLimit = 1000, int queueLimit = 10000)
        {
            if (drainPerFrameLimit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(drainPerFrameLimit), drainPerFrameLimit, null);
            }

            if (queueLimit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(queueLimit), queueLimit, null);
            }

            _drainPerFrameLimit = drainPerFrameLimit;
            _queueLimit = queueLimit;
            _drainBudget = drainPerFrameLimit;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureMainThread()
        {
            _mainContext = SynchronizationContext.Current;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void CaptureMainThreadInEditor()
        {
            CaptureMainThread();

            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
            {
                CaptureMainThread();
            }
        }
#endif

        /// <summary>
        /// Called on the main thread to log a message, so Unity state is safe to use here.
        /// </summary>
        /// <param name="message">The message to write.</param>
        /// <param name="level">The level to log at.</param>
        protected abstract void WriteOnMainThread(string message, LogLevel level);

        /// <inheritdoc/>
        [HideInCallstack]
        protected sealed override void WriteSerial(string message, LogLevel level)
        {
            var entry = new Entry(message, level);

            if (OnMainThread)
            {
                // flush older queued messages first to keep ordering
                Drain();

                if (_queue.IsEmpty)
                {
                    RunOnMain(entry);
                    return;
                }
            }

            // Serialized by the base class, so the limit check cannot race another writer.
            if (Volatile.Read(ref _queued) >= _queueLimit)
            {
                if (Interlocked.Increment(ref _dropped) == 1)
                {
                    Debug.LogWarning($"{GetType().Name}: queue limit of {_queueLimit} reached, dropping new logs.");
                }

                return;
            }

            Interlocked.Increment(ref _queued);
            _queue.Enqueue(entry);
            ScheduleDrain();
        }

        /// <inheritdoc/>
        [HideInCallstack]
        protected sealed override void OnClosed()
        {
            if (OnMainThread || _mainContext == null)
            {
                FinishClose();
                return;
            }

            _mainContext.Post(CloseCallback, this);
        }

        /// <summary>
        /// Called when this sink is closed, on the main thread unless no main thread has been captured.
        /// </summary>
        protected virtual void OnSinkClosed() { }

        [HideInCallstack]
        private void FinishClose()
        {
            if (OnMainThread)
            {
                // Everything accepted gets delivered on the way out, limit or not.
                _drainBudget = int.MaxValue;
                Drain();
            }

            lock (_pumpLock)
            {
                _closed = true;
            }

            while (TryDequeue(out _)) { }

            OnSinkClosed();
        }

        [HideInCallstack]
        private void ScheduleDrain()
        {
            if (_mainContext == null)
            {
                return;
            }

            if (Interlocked.Exchange(ref _drainScheduled, PendingDrain) == PendingDrain)
            {
                return;
            }

            _mainContext.Post(DrainCallback, this);
        }

        [HideInCallstack]
        private void DrainFrame()
        {
            // Reset BEFORE draining: anything enqueued after this point triggers a fresh post.
            Volatile.Write(ref _drainScheduled, EmptyDrain);

            _drainBudget = _drainPerFrameLimit;
            Drain();
        }

        [HideInCallstack]
        private void Drain()
        {
            while (_drainBudget > 0 && TryDequeue(out var message))
            {
                _drainBudget--;

                try
                {
                    RunOnMain(message);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            if (!_queue.IsEmpty)
            {
                if (!_throttled)
                {
                    _throttled = true;
                    Debug.LogWarning($"{GetType().Name}: drain limit of {_drainPerFrameLimit} reached, deferring logs.");
                }

                ScheduleDrain();
                return;
            }

            _throttled = false;

            int dropped = Interlocked.Exchange(ref _dropped, 0);
            if (dropped > 0)
            {
                Debug.LogWarning($"{GetType().Name}: dropped {dropped} logs while the queue was full.");
            }
        }

        private bool TryDequeue(out Entry entry)
        {
            if (!_queue.TryDequeue(out entry))
            {
                return false;
            }

            Interlocked.Decrement(ref _queued);
            return true;
        }

        [HideInCallstack]
        private void RunOnMain(Entry entry)
        {
            lock (_pumpLock)
            {
                if (_closed)
                {
                    return;
                }

                WriteOnMainThread(entry.Message, entry.Level);
            }
        }
    }
}

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
        private static readonly SendOrPostCallback DrainCallback = state => ((UnitySafeSink)state).Drain();

        private const int EmptyDrain = 0;
        private const int PendingDrain = 1;

        private int _drainScheduled;

        private readonly ConcurrentQueue<Entry> _queue = new();
        private readonly object _pumpLock = new();
        private bool _closed;

        private static bool OnMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureMainThread()
        {
            _mainContext = SynchronizationContext.Current;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

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
            if (OnMainThread)
            {
                // flush older queued messages first to keep ordering
                Drain();
                RunOnMain(new Entry(message, level));
                return;
            }

            _queue.Enqueue(new Entry(message, level));
            ScheduleDrain();
        }

        /// <inheritdoc/>
        [HideInCallstack]
        protected sealed override void OnClosed()
        {
            if (OnMainThread)
            {
                Drain();
            }

            lock (_pumpLock)
            {
                _closed = true;
            }

            while (_queue.TryDequeue(out _)) { }
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
        private void Drain()
        {
            // Reset BEFORE draining: anything enqueued after this point triggers a fresh post.
            Volatile.Write(ref _drainScheduled, EmptyDrain);

            while (_queue.TryDequeue(out var message))
            {
                RunOnMain(message);
            }
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

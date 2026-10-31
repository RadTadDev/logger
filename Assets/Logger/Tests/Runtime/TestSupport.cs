using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using NUnit.Framework;

namespace RadTadDev.Logger.Tests
{
    /// <summary>
    /// Reflection access to logger internals. This is the only place that knows private names, so a
    /// refactor of them needs fixing here and nowhere else.
    /// </summary>
    internal static class Internals
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;

        private static readonly Type SinkManagerType = typeof(Log).Assembly.GetType("RadTadDev.Logger.SinkManager");
        private static readonly FieldInfo SinksField = SinkManagerType.GetField("_sinks", Private);
        private static readonly MethodInfo ResetMethod = SinkManagerType.GetMethod("Reset", Private);
        private static readonly MethodInfo ResetOnPlayMethod = SinkManagerType.GetMethod("ResetOnPlay", Private);
        private static readonly FieldInfo MainContextField = typeof(UnitySafeSink).GetField("_mainContext", Private);
        private static readonly FieldInfo MainThreadIdField = typeof(UnitySafeSink).GetField("_mainThreadId", Private);

        /// <summary>
        /// The registered sinks.
        /// </summary>
        public static ISink[] Sinks
        {
            get => (ISink[])SinksField.GetValue(null);
            set => SinksField.SetValue(null, value);
        }

        /// <summary>
        /// The synchronization context main thread work is posted to.
        /// </summary>
        public static SynchronizationContext MainContext
        {
            get => (SynchronizationContext)MainContextField.GetValue(null);
            set => MainContextField.SetValue(null, value);
        }

        /// <summary>
        /// The id of the thread sinks treat as the main thread.
        /// </summary>
        public static int MainThreadId
        {
            get => (int)MainThreadIdField.GetValue(null);
            set => MainThreadIdField.SetValue(null, value);
        }

        /// <summary>
        /// Runs the teardown that <c>Application.quitting</c> triggers.
        /// </summary>
        public static void Reset() => Invoke(ResetMethod);

        /// <summary>
        /// Runs the startup that entering play mode triggers.
        /// </summary>
        public static void ResetOnPlay() => Invoke(ResetOnPlayMethod);

        private static void Invoke(MethodInfo method)
        {
            try
            {
                method.Invoke(null, null);
            }
            catch (TargetInvocationException e)
            {
                ExceptionDispatchRethrow(e.InnerException);
            }
        }

        private static void ExceptionDispatchRethrow(Exception e) =>
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw();
    }

    /// <summary>
    /// A synchronization context pumped by hand, so tests decide exactly when "the next frame" happens.
    /// </summary>
    internal sealed class PumpContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object State)> _posted = new();
        private int _postCount;

        /// <summary>
        /// How many callbacks have been posted.
        /// </summary>
        public int PostCount => Volatile.Read(ref _postCount);

        /// <summary>
        /// Exceptions that escaped a posted callback.
        /// </summary>
        public List<Exception> Escaped { get; } = new();

        /// <inheritdoc/>
        public override void Post(SendOrPostCallback d, object state)
        {
            Interlocked.Increment(ref _postCount);
            _posted.Enqueue((d, state));
        }

        /// <summary>
        /// Runs the callbacks posted so far.
        /// </summary>
        public void Pump()
        {
            for (int remaining = _posted.Count; remaining > 0 && _posted.TryDequeue(out var item); remaining--)
            {
                try
                {
                    item.Callback(item.State);
                }
                catch (Exception e)
                {
                    Escaped.Add(e);
                }
            }
        }
    }

    /// <summary>
    /// A <see cref="ThreadSafeSink"/> that records what it receives.
    /// </summary>
    internal sealed class RecordingSink : ThreadSafeSink
    {
        private int _active;
        private int _closeCount;

        /// <summary>
        /// Messages received, in order.
        /// </summary>
        public List<string> Messages { get; } = new();

        /// <summary>
        /// Levels received, in order.
        /// </summary>
        public List<LogLevel> Levels { get; } = new();

        /// <summary>
        /// The most writers ever inside <see cref="WriteSerial"/> at once.
        /// </summary>
        public int MaxConcurrentWriters { get; private set; }

        /// <summary>
        /// Writes that arrived after <see cref="OnClosed"/>.
        /// </summary>
        public int WritesAfterClose { get; private set; }

        /// <summary>
        /// How many times <see cref="OnClosed"/> ran.
        /// </summary>
        public int CloseCount => Volatile.Read(ref _closeCount);

        /// <inheritdoc/>
        protected override void WriteSerial(string message, LogLevel level)
        {
            MaxConcurrentWriters = Math.Max(MaxConcurrentWriters, Interlocked.Increment(ref _active));

            if (CloseCount > 0)
            {
                WritesAfterClose++;
            }

            Messages.Add(message);
            Levels.Add(level);
            Interlocked.Decrement(ref _active);
        }

        /// <inheritdoc/>
        protected override void OnClosed() => Interlocked.Increment(ref _closeCount);
    }

    /// <summary>
    /// A <see cref="UnitySafeSink"/> that records what it receives and where.
    /// </summary>
    internal sealed class RecordingUnitySink : UnitySafeSink
    {
        private int _attempts;

        /// <summary>
        /// Creates a sink. Limits default to off so tests only meet them on purpose.
        /// </summary>
        /// <param name="drainPerFrameLimit">Maximum logs delivered per frame.</param>
        /// <param name="queueLimit">Maximum logs queued.</param>
        public RecordingUnitySink(int drainPerFrameLimit = int.MaxValue, int queueLimit = int.MaxValue)
            : base(drainPerFrameLimit, queueLimit) { }

        /// <summary>
        /// How many times <see cref="OnSinkClosed"/> ran.
        /// </summary>
        public int SinkClosedCount { get; private set; }

        /// <summary>
        /// The id of the thread <see cref="OnSinkClosed"/> ran on.
        /// </summary>
        public int CleanupThreadId { get; private set; }

        /// <summary>
        /// Messages received, in order.
        /// </summary>
        public List<string> Messages { get; } = new();

        /// <summary>
        /// The ids of the threads messages were delivered on.
        /// </summary>
        public HashSet<int> ThreadIds { get; } = new();

        /// <summary>
        /// The zero-based delivery attempt that throws, or -1 for none.
        /// </summary>
        public int FailOnAttempt { get; set; } = -1;

        /// <summary>
        /// Runs after each delivery.
        /// </summary>
        public Action<string> AfterWrite { get; set; }

        /// <inheritdoc/>
        protected override void WriteOnMainThread(string message, LogLevel level)
        {
            ThreadIds.Add(Thread.CurrentThread.ManagedThreadId);

            if (_attempts++ == FailOnAttempt)
            {
                throw new InvalidOperationException("sink failure");
            }

            Messages.Add(message);
            AfterWrite?.Invoke(message);
        }

        /// <inheritdoc/>
        protected override void OnSinkClosed()
        {
            SinkClosedCount++;
            CleanupThreadId = Thread.CurrentThread.ManagedThreadId;
        }
    }

    /// <summary>
    /// An <see cref="ISink"/> that fails on demand.
    /// </summary>
    internal sealed class ThrowingSink : ISink
    {
        /// <summary>
        /// Whether <see cref="Write"/> throws.
        /// </summary>
        public bool ThrowOnWrite { get; set; }

        /// <summary>
        /// Whether <see cref="Close"/> throws.
        /// </summary>
        public bool ThrowOnClose { get; set; }

        /// <inheritdoc/>
        public void Write(string message, LogLevel level)
        {
            if (ThrowOnWrite)
            {
                throw new InvalidOperationException("sink failure");
            }
        }

        /// <inheritdoc/>
        public void Close()
        {
            if (ThrowOnClose)
            {
                throw new InvalidOperationException("close failure");
            }
        }
    }

    /// <summary>
    /// An object whose <see cref="ToString"/> throws.
    /// </summary>
    internal sealed class ThrowingToString
    {
        /// <inheritdoc/>
        public override string ToString() => throw new InvalidOperationException("ToString failure");
    }

    /// <summary>
    /// Isolates each test from the logger's global state. Sinks are swapped out rather than removed,
    /// so the real default sink survives the test untouched.
    /// </summary>
    public abstract class LoggerFixture
    {
        private static readonly TimeSpan ThreadTimeout = TimeSpan.FromSeconds(30);

        private readonly List<ISink> _added = new();
        private ISink[] _savedSinks;
        private SynchronizationContext _savedContext;
        private int _savedThreadId;
        private LogLevel _savedMinimumLevel;

        /// <summary>
        /// Stands in for the main thread's synchronization context.
        /// </summary>
        internal PumpContext MainLoop { get; private set; }

        /// <summary>
        /// The sinks that were registered before this test isolated the logger.
        /// </summary>
        protected ISink[] OriginalSinks => _savedSinks;

        /// <summary>
        /// Whether to replace the real main loop with <see cref="MainLoop"/>.
        /// </summary>
        protected virtual bool UsePumpContext => true;

        /// <summary>
        /// Saves global state and starts the test with no sinks.
        /// </summary>
        [SetUp]
        public void IsolateLogger()
        {
            RuntimeHelpers.RunClassConstructor(typeof(Log).TypeHandle);

            _savedSinks = Internals.Sinks;
            _savedContext = Internals.MainContext;
            _savedThreadId = Internals.MainThreadId;
            _savedMinimumLevel = Log.MinimumLevel;

            Internals.Sinks = Array.Empty<ISink>();
            Log.MinimumLevel = LogLevel.Info;

            if (UsePumpContext)
            {
                MainLoop = new PumpContext();
                Internals.MainContext = MainLoop;
                Internals.MainThreadId = Thread.CurrentThread.ManagedThreadId;
            }
        }

        /// <summary>
        /// Closes the test's sinks and restores global state.
        /// </summary>
        [TearDown]
        public void RestoreLogger()
        {
            foreach (ISink sink in _added)
            {
                try
                {
                    sink?.Close();
                }
                catch (InvalidOperationException)
                {
                    // ThrowingSink doing its job
                }
            }

            _added.Clear();

            Internals.Sinks = _savedSinks;
            Internals.MainContext = _savedContext;
            Internals.MainThreadId = _savedThreadId;
            Log.MinimumLevel = _savedMinimumLevel;
        }

        /// <summary>
        /// Registers a sink for this test.
        /// </summary>
        /// <param name="sink">The sink.</param>
        /// <typeparam name="T">The sink type.</typeparam>
        /// <returns>The sink.</returns>
        protected T Add<T>(T sink) where T : ISink
        {
            _added.Add(sink);
            Log.AddSink(sink);
            return sink;
        }

        /// <summary>
        /// Logs through the public API that matches a level.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <param name="message">The message.</param>
        protected static void LogAt(LogLevel level, object message)
        {
            switch (level)
            {
                case LogLevel.Trace:
                    Log.Trace(message);
                    break;
                case LogLevel.Info:
                    Log.Info(message);
                    break;
                case LogLevel.Warn:
                    Log.Warn(message);
                    break;
                case LogLevel.Error:
                    Log.Error(message);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }
        }

        /// <summary>
        /// Runs <paramref name="body"/> on several threads released together, failing the test if any
        /// throws or hangs. Hung threads are background threads, so they cannot wedge the editor.
        /// </summary>
        /// <param name="count">How many threads to run.</param>
        /// <param name="body">The work, given the thread's index.</param>
        /// <param name="whileWaiting">Runs repeatedly on the calling thread until they finish.</param>
        protected static void RunThreads(int count, Action<int> body, Action whileWaiting = null)
        {
            var failures = new ConcurrentQueue<Exception>();
            var threads = new Thread[count];

            using (var start = new Barrier(count))
            {
                for (int i = 0; i < count; i++)
                {
                    int index = i;

                    threads[i] = new Thread(() =>
                    {
                        try
                        {
                            start.SignalAndWait();
                            body(index);
                        }
                        catch (Exception e)
                        {
                            failures.Enqueue(e);
                        }
                    }) { IsBackground = true };

                    threads[i].Start();
                }

                DateTime deadline = DateTime.UtcNow + ThreadTimeout;

                foreach (Thread thread in threads)
                {
                    while (!thread.Join(1))
                    {
                        whileWaiting?.Invoke();
                        Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "Worker threads hung.");
                    }
                }
            }

            whileWaiting?.Invoke();
            Assert.That(failures, Is.Empty);
        }
    }
}

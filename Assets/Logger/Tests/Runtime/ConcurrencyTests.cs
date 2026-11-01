using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace RadTadDev.Logger.Tests
{
    /// <summary>
    /// Stress tests. Sizes are tuned to run in well under a second each while still interleaving.
    /// </summary>
    public class ConcurrencyTests : LoggerFixture
    {
        private const int Threads = 8;
        private const int PerThread = 2000;

        /// <summary>
        /// Writes never overlap inside a sink, and none are lost.
        /// </summary>
        [Test]
        public void ThreadSafeSink_SerializesWriters()
        {
            var sink = Add(new RecordingSink());

            RunThreads(Threads, _ =>
            {
                for (var i = 0; i < PerThread; i++)
                {
                    Log.Info("m");
                }
            });

            Assert.That(sink.Messages, Has.Count.EqualTo(Threads * PerThread));
            Assert.That(sink.MaxConcurrentWriters, Is.EqualTo(1));
        }

        /// <summary>
        /// Every message arrives exactly once, on the main thread, in order per producing thread.
        /// </summary>
        [Test]
        public void UnitySafeSink_DeliversExactlyOnceInOrderPerThread()
        {
            var sink = Add(new RecordingUnitySink());
            var mainMessages = 0;

            RunThreads(Threads, thread =>
            {
                for (var i = 0; i < PerThread; i++)
                {
                    Log.Info($"{thread}:{i}");
                }
            }, () =>
            {
                Log.Info($"main:{mainMessages++}");
                MainLoop.Pump();
            });

            MainLoop.Pump();

            var next = new Dictionary<string, int>();

            foreach (var message in sink.Messages)
            {
                var parts = message.Substring(message.LastIndexOf(' ') + 1).Split(':');
                next.TryGetValue(parts[0], out var expected);

                Assert.That(int.Parse(parts[1]), Is.EqualTo(expected), $"Out of order on producer {parts[0]}.");
                next[parts[0]] = expected + 1;
            }

            Assert.That(sink.Messages, Has.Count.EqualTo(Threads * PerThread + mainMessages));
            Assert.That(sink.ThreadIds, Is.EqualTo(new[] { Thread.CurrentThread.ManagedThreadId }));
            Assert.That(MainLoop.Escaped, Is.Empty);
        }

        /// <summary>
        /// Several sinks each see the full stream.
        /// </summary>
        [Test]
        public void MultipleSinks_EachReceiveEverything()
        {
            var sinks = Enumerable.Range(0, 3).Select(_ => Add(new RecordingSink())).ToArray();

            RunThreads(Threads, _ =>
            {
                for (var i = 0; i < 500; i++)
                {
                    Log.Info("m");
                }
            });

            Assert.That(sinks.Select(s => s.Messages.Count), Is.All.EqualTo(Threads * 500));
        }

        /// <summary>
        /// Sinks come and go while others log, without errors or writes after close.
        /// </summary>
        [Test]
        public void AddAndRemove_UnderLoad_IsSafe()
        {
            var stable = Add(new RecordingSink());
            var stop = 0;
            var logged = 0;

            RunThreads(8, thread =>
            {
                if (thread < 4)
                {
                    while (Volatile.Read(ref stop) == 0)
                    {
                        Log.Info("m");
                        Interlocked.Increment(ref logged);
                    }

                    return;
                }

                try
                {
                    for (var i = 0; i < 2000; i++)
                    {
                        var sink = new RecordingSink();
                        Log.AddSink(sink);
                        Log.RemoveSink(sink);

                        Assert.That(sink.CloseCount, Is.EqualTo(1));
                        Assert.That(sink.WritesAfterClose, Is.Zero);
                    }
                }
                finally
                {
                    Volatile.Write(ref stop, 1);
                }
            });

            Assert.That(Internals.Sinks, Is.EqualTo(new[] { stable }));
            Assert.That(stable.Messages, Has.Count.EqualTo(logged));
        }

        /// <summary>
        /// Racing to add one sink still registers it once.
        /// </summary>
        [Test]
        public void AddSink_Racing_RegistersOnce()
        {
            var sink = new RecordingSink();

            RunThreads(Threads, _ => Log.AddSink(sink));

            Assert.That(Internals.Sinks, Has.Length.EqualTo(1));
        }

        /// <summary>
        /// Different sinks added at once are all kept.
        /// </summary>
        [Test]
        public void AddSink_RacingDifferentSinks_KeepsAll()
        {
            RunThreads(Threads, _ => Log.AddSink(new RecordingSink()));

            Assert.That(Internals.Sinks, Has.Length.EqualTo(Threads));
        }

        /// <summary>
        /// Racing to remove one sink closes it once.
        /// </summary>
        [Test]
        public void RemoveSink_Racing_ClosesOnce()
        {
            var sink = Add(new RecordingSink());

            RunThreads(Threads, _ => Log.RemoveSink(sink));

            Assert.That(sink.CloseCount, Is.EqualTo(1));
            Assert.That(Internals.Sinks, Is.Empty);
        }

        /// <summary>
        /// Once close has run, a racing writer never gets through.
        /// </summary>
        [Test]
        public void Close_RacingWrites_NeverWritesAfterClose()
        {
            for (var round = 0; round < 200; round++)
            {
                var sink = new RecordingSink();

                RunThreads(4, thread =>
                {
                    if (thread == 0)
                    {
                        sink.Close();
                        return;
                    }

                    for (var i = 0; i < 20; i++)
                    {
                        sink.Write("m", LogLevel.Info);
                    }
                });

                Assert.That(sink.WritesAfterClose, Is.Zero);
                Assert.That(sink.CloseCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Closing from a worker while the main thread drains is safe.
        /// </summary>
        [Test]
        public void UnitySafeSink_CloseRacingDrain_IsSafe()
        {
            for (var round = 0; round < 100; round++)
            {
                var sink = new RecordingUnitySink();
                Log.AddSink(sink);

                RunThreads(3, thread =>
                {
                    for (var i = 0; i < 20; i++)
                    {
                        Log.Info("m");
                    }

                    if (thread == 0)
                    {
                        Log.RemoveSink(sink);
                    }
                }, MainLoop.Pump);

                Assert.That(MainLoop.Escaped, Is.Empty);
                Assert.That(Internals.Sinks, Is.Empty);
            }
        }

        /// <summary>
        /// A write callback that logs while a worker closes the same sink cannot deadlock the two locks.
        /// The stand-in main thread is a worker here so the test fails rather than hangs.
        /// </summary>
        [Test]
        public void ReentrantLog_RacingClose_DoesNotDeadlock()
        {
            var sink = new RecordingUnitySink();
            Log.AddSink(sink);

            using var inDrain = new ManualResetEventSlim();
            using var closing = new ManualResetEventSlim();
            var first = true;

            sink.AfterWrite = _ =>
            {
                if (!first)
                {
                    return;
                }

                first = false;
                inDrain.Set();
                closing.Wait(TimeSpan.FromSeconds(1));
                Thread.Sleep(50);
                Log.Info("reentrant");
            };

            RunThreads(1, _ => Log.Info("queued"));

            RunThreads(2, thread =>
            {
                if (thread == 0)
                {
                    Internals.MainThreadId = Thread.CurrentThread.ManagedThreadId;
                    MainLoop.Pump();
                    return;
                }

                inDrain.Wait(TimeSpan.FromSeconds(5));
                closing.Set();
                Log.RemoveSink(sink);
            });
        }

        /// <summary>
        /// Changing the filter mid-stream is safe.
        /// </summary>
        [Test]
        public void MinimumLevel_ChangingUnderLoad_IsSafe()
        {
            var levels = new[] { LogLevel.Trace, LogLevel.Info, LogLevel.Warn, LogLevel.Error };
            var sink = Add(new RecordingSink());

            RunThreads(Threads, thread =>
            {
                for (var i = 0; i < 1000; i++)
                {
                    if (thread == 0)
                    {
                        Log.MinimumLevel = levels[i % levels.Length];
                    }

                    LogAt(levels[i % levels.Length], "m");
                }
            });

            Assert.That(sink.Messages.Count, Is.EqualTo(sink.Levels.Count));
        }
    }
}

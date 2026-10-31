using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RadTadDev.Logger.Tests
{
    /// <summary>
    /// Marshalling to the main thread, driven by a hand-pumped main loop so timing is deterministic.
    /// </summary>
    public class UnitySafeSinkTests : LoggerFixture
    {
        /// <summary>
        /// Main thread logs arrive immediately, with no frame of delay.
        /// </summary>
        [Test]
        public void MainThreadWrite_IsDeliveredSynchronously()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink());

            Log.Info("m");

            Assert.That(sink.Messages, Has.Count.EqualTo(1));
            Assert.That(MainLoop.PostCount, Is.Zero);
        }

        /// <summary>
        /// Worker logs wait for the main loop.
        /// </summary>
        [Test]
        public void WorkerWrite_WaitsForMainLoop()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink());

            RunThreads(1, _ => Log.Info("m"));

            Assert.That(sink.Messages, Is.Empty);

            MainLoop.Pump();

            Assert.That(sink.Messages, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Worker logs are delivered on the main thread.
        /// </summary>
        [Test]
        public void WorkerWrite_IsDeliveredOnMainThread()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink());

            RunThreads(4, _ => Log.Info("m"));
            MainLoop.Pump();

            Assert.That(sink.ThreadIds, Is.EqualTo(new[] { Thread.CurrentThread.ManagedThreadId }));
        }

        /// <summary>
        /// Queued messages come out first in, first out.
        /// </summary>
        [Test]
        public void WorkerWrites_KeepOrder()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink());

            RunThreads(1, _ =>
            {
                for (int i = 0; i < 100; i++)
                {
                    Log.Info(i);
                }
            });
            MainLoop.Pump();

            var expected = Enumerable.Range(0, 100).Select(i => $"[Info] [UnitySafeSinkTests]: {i}");
            Assert.That(sink.Messages, Is.EqualTo(expected));
        }

        /// <summary>
        /// A main thread log flushes earlier worker logs first, so output matches logging order.
        /// </summary>
        [Test]
        public void MainThreadWrite_FlushesQueuedWorkerWritesFirst()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink());

            RunThreads(1, _ => Log.Info("worker"));
            Log.Info("main");

            Assert.That(sink.Messages[0], Does.EndWith("worker"));
            Assert.That(sink.Messages[1], Does.EndWith("main"));
        }

        /// <summary>
        /// A burst schedules one drain rather than one per message.
        /// </summary>
        [Test]
        public void WorkerBurst_CoalescesIntoOneDrain()
        {
            Add(new RecordingUnitySink());

            RunThreads(4, _ =>
            {
                for (int i = 0; i < 100; i++)
                {
                    Log.Info("m");
                }
            });

            Assert.That(MainLoop.PostCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Each drain re-arms scheduling for the next batch.
        /// </summary>
        [Test]
        public void DrainedSink_SchedulesAgain()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink());

            for (int i = 1; i <= 3; i++)
            {
                RunThreads(1, _ => Log.Info("m"));
                MainLoop.Pump();

                Assert.That(MainLoop.PostCount, Is.EqualTo(i));
                Assert.That(sink.Messages, Has.Count.EqualTo(i));
            }
        }

        /// <summary>
        /// A message arriving while a drain runs is not stranded.
        /// </summary>
        [Test]
        public void WorkerWrite_DuringDrain_IsNotLost()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink());
            bool logged = false;

            sink.AfterWrite = _ =>
            {
                if (!logged)
                {
                    logged = true;
                    RunThreads(1, __ => Log.Info("during"));
                }
            };

            RunThreads(1, _ => Log.Info("first"));
            MainLoop.Pump();
            MainLoop.Pump();

            Assert.That(sink.Messages, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// A sink that logs while writing neither hangs nor loses the nested message.
        /// </summary>
        [Test]
        public void ReentrantLog_FromWrite_IsDelivered()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink());
            bool logged = false;

            sink.AfterWrite = _ =>
            {
                if (!logged)
                {
                    logged = true;
                    Log.Info("nested");
                }
            };

            Log.Info("outer");

            Assert.That(sink.Messages, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// Closing on the main thread delivers what was already accepted.
        /// </summary>
        [Test]
        public void Close_OnMainThread_FlushesQueue()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink());

            RunThreads(1, _ => Log.Info("m"));
            Log.RemoveSink(sink);

            Assert.That(sink.Messages, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// A drain still posted when the sink closes does nothing.
        /// </summary>
        [Test]
        public void PendingDrain_AfterClose_IsHarmless()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink());

            RunThreads(1, _ => Log.Info("m"));
            sink.Close();
            int delivered = sink.Messages.Count;
            MainLoop.Pump();

            Assert.That(sink.Messages, Has.Count.EqualTo(delivered));
            Assert.That(MainLoop.Escaped, Is.Empty);
        }

        /// <summary>
        /// Closing from a worker still delivers what was already accepted (audit #8).
        /// </summary>
        [Test]
        public void Close_OnWorkerThread_DeliversAcceptedMessages()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink());

            RunThreads(1, _ =>
            {
                for (int i = 0; i < 10; i++)
                {
                    Log.Info("m");
                }

                Log.RemoveSink(sink);
            });
            MainLoop.Pump();

            Assert.That(sink.Messages, Has.Count.EqualTo(10));
        }

        /// <summary>
        /// Cleanup waits for those messages and runs on the main thread.
        /// </summary>
        [Test]
        public void Close_OnWorkerThread_RunsCleanupOnMainThreadAfterDelivery()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink());

            RunThreads(1, _ =>
            {
                Log.Info("m");
                Log.RemoveSink(sink);
            });

            Assert.That(sink.SinkClosedCount, Is.Zero);

            MainLoop.Pump();

            Assert.That(sink.SinkClosedCount, Is.EqualTo(1));
            Assert.That(sink.Messages, Has.Count.EqualTo(1));
            Assert.That(sink.CleanupThreadId, Is.EqualTo(Thread.CurrentThread.ManagedThreadId));
        }

        /// <summary>
        /// A closing sink takes no new messages while it waits for the main thread.
        /// </summary>
        [Test]
        public void Close_OnWorkerThread_RefusesNewWrites()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink());

            RunThreads(1, _ => sink.Close());
            sink.Write("late", LogLevel.Info);
            MainLoop.Pump();

            Assert.That(sink.Messages, Is.Empty);
        }

        /// <summary>
        /// A worker close delivers everything accepted, whatever the limit.
        /// </summary>
        [Test]
        public void DrainLimit_DoesNotApplyWhenClosingOnWorkerThread()
        {
            LogAssert.ignoreFailingMessages = true;
            RecordingUnitySink sink = Add(new RecordingUnitySink(drainPerFrameLimit: 2));

            RunThreads(1, _ =>
            {
                for (int i = 0; i < 10; i++)
                {
                    Log.Info("m");
                }

                Log.RemoveSink(sink);
            });
            MainLoop.Pump();

            Assert.That(sink.Messages, Has.Count.EqualTo(10));
        }

        /// <summary>
        /// A failing delivery does not cost the rest of the batch (audit #4).
        /// </summary>
        [Test]
        public void Drain_ThrowingWrite_DeliversTheRest()
        {
            LogAssert.ignoreFailingMessages = true;
            RecordingUnitySink sink = Add(new RecordingUnitySink());
            sink.FailOnAttempt = 1;

            RunThreads(1, _ =>
            {
                for (int i = 0; i < 5; i++)
                {
                    Log.Info("m");
                }
            });
            MainLoop.Pump();

            Assert.That(sink.Messages, Has.Count.EqualTo(4));
        }

        /// <summary>
        /// A failure flushing old messages does not cost the message being logged now (audit #4).
        /// </summary>
        [Test]
        public void MainThreadWrite_FailedFlush_StillDeliversCurrentMessage()
        {
            LogAssert.ignoreFailingMessages = true;
            RecordingUnitySink sink = Add(new RecordingUnitySink());
            sink.FailOnAttempt = 0;

            RunThreads(1, _ => Log.Info("worker"));
            Log.Info("main");

            Assert.That(sink.Messages, Has.Some.EndsWith("main"));
        }

        /// <summary>
        /// A backlog cannot hold the main thread for many frames at once (audit #7).
        /// </summary>
        [Test]
        public void Drain_LargeBacklog_DoesNotStallMainThread()
        {
            const int messages = 10000;
            const long slowWriteTicksPerMicrosecond = 50;

            RecordingUnitySink sink = Add(new RecordingUnitySink(1000, messages));
            LogAssert.Expect(LogType.Warning, new Regex("drain limit"));

            sink.AfterWrite = _ =>
            {
                long end = Stopwatch.GetTimestamp() + (Stopwatch.Frequency * slowWriteTicksPerMicrosecond / 1000000);

                while (Stopwatch.GetTimestamp() < end) { }
            };

            RunThreads(1, _ =>
            {
                for (int i = 0; i < messages; i++)
                {
                    Log.Info("m");
                }
            });

            Stopwatch frame = Stopwatch.StartNew();
            MainLoop.Pump();
            frame.Stop();

            Assert.That(frame.ElapsedMilliseconds, Is.LessThan(100));
        }

        /// <summary>
        /// One delivery failing is reported.
        /// </summary>
        [Test]
        public void Drain_ThrowingWrite_IsReported()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink());
            sink.FailOnAttempt = 0;
            LogAssert.Expect(LogType.Exception, new Regex("sink failure"));

            RunThreads(1, _ => Log.Info("m"));
            MainLoop.Pump();

            Assert.That(MainLoop.Escaped, Is.Empty);
        }

        /// <summary>
        /// A failed flush does not skip the subclass cleanup hook.
        /// </summary>
        [Test]
        public void Close_ThrowingFlush_StillRunsCleanup()
        {
            LogAssert.ignoreFailingMessages = true;
            RecordingUnitySink sink = Add(new RecordingUnitySink());
            sink.FailOnAttempt = 0;

            RunThreads(1, _ => Log.Info("m"));
            sink.Close();

            Assert.That(sink.SinkClosedCount, Is.EqualTo(1));
        }

        /// <summary>
        /// The cleanup hook runs once however often the sink is closed.
        /// </summary>
        [Test]
        public void OnSinkClosed_RunsOnce()
        {
            var sink = new RecordingUnitySink();

            sink.Close();
            sink.Close();

            Assert.That(sink.SinkClosedCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Limits must leave the sink able to make progress.
        /// </summary>
        /// <param name="drain">The drain limit.</param>
        /// <param name="queue">The queue limit.</param>
        [TestCase(0, 1)]
        [TestCase(-1, 1)]
        [TestCase(1, 0)]
        [TestCase(1, -1)]
        public void Constructor_NonPositiveLimits_Throw(int drain, int queue)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingUnitySink(drain, queue));
        }

        /// <summary>
        /// A frame delivers up to the limit, and the rest follow in order on later frames.
        /// </summary>
        [Test]
        public void DrainLimit_SpreadsBacklogAcrossFramesInOrder()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink(drainPerFrameLimit: 4));
            LogAssert.Expect(LogType.Warning, new Regex("drain limit of 4"));
            LogBurst(10);

            MainLoop.Pump();
            Assert.That(sink.Messages, Has.Count.EqualTo(4));

            MainLoop.Pump();
            Assert.That(sink.Messages, Has.Count.EqualTo(8));

            MainLoop.Pump();
            Assert.That(sink.Messages, Is.EqualTo(Expected(10)));
        }

        /// <summary>
        /// One backlog warns once, however many frames it takes to clear.
        /// </summary>
        [Test]
        public void DrainLimit_WarnsOncePerBacklog()
        {
            Add(new RecordingUnitySink(drainPerFrameLimit: 1));
            LogAssert.Expect(LogType.Warning, new Regex("drain limit of 1"));
            LogBurst(5);

            for (int i = 0; i < 5; i++)
            {
                MainLoop.Pump();
            }

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// Exactly hitting the limit defers nothing, so nothing warns.
        /// </summary>
        [Test]
        public void DrainLimit_ExactlyMet_DoesNotWarn()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink(drainPerFrameLimit: 5));
            LogBurst(5);

            MainLoop.Pump();

            Assert.That(sink.Messages, Has.Count.EqualTo(5));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// A main thread log waits its turn behind a backlog rather than jumping it.
        /// </summary>
        [Test]
        public void DrainLimit_MainThreadWrite_KeepsOrderBehindBacklog()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink(drainPerFrameLimit: 2));
            LogAssert.Expect(LogType.Warning, new Regex("drain limit"));
            LogBurst(5);

            Log.Info(5);

            for (int i = 0; i < 3; i++)
            {
                MainLoop.Pump();
            }

            Assert.That(sink.Messages, Is.EqualTo(Expected(6)));
        }

        /// <summary>
        /// Logging on the main thread cannot spend more than one frame of budget on a backlog.
        /// </summary>
        [Test]
        public void DrainLimit_AppliesToMainThreadFlushes()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink(drainPerFrameLimit: 2));
            LogAssert.Expect(LogType.Warning, new Regex("drain limit"));
            LogBurst(10);

            for (int i = 0; i < 5; i++)
            {
                Log.Info("main");
            }

            Assert.That(sink.Messages, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// Closing delivers everything accepted, whatever the limit.
        /// </summary>
        [Test]
        public void DrainLimit_DoesNotApplyWhenClosing()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink(drainPerFrameLimit: 2));
            LogBurst(10);

            Log.RemoveSink(sink);

            Assert.That(sink.Messages, Has.Count.EqualTo(10));
        }

        /// <summary>
        /// A full queue drops the newest logs, keeping the ones that came first.
        /// </summary>
        [Test]
        public void QueueLimit_DropsNewest()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink(queueLimit: 3));
            LogAssert.Expect(LogType.Warning, new Regex("queue limit of 3"));
            LogAssert.Expect(LogType.Warning, new Regex("dropped 7 logs"));

            LogBurst(10);
            MainLoop.Pump();

            Assert.That(sink.Messages, Is.EqualTo(Expected(3)));
        }

        /// <summary>
        /// Exactly filling the queue loses nothing, so nothing warns.
        /// </summary>
        [Test]
        public void QueueLimit_ExactlyMet_DoesNotWarn()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink(queueLimit: 5));

            LogBurst(5);
            MainLoop.Pump();

            Assert.That(sink.Messages, Has.Count.EqualTo(5));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// Once drained, the sink accepts logs again and a later overflow warns afresh.
        /// </summary>
        [Test]
        public void QueueLimit_RecoversAfterDrain()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink(queueLimit: 2));

            for (int i = 0; i < 2; i++)
            {
                LogAssert.Expect(LogType.Warning, new Regex("queue limit of 2"));
                LogAssert.Expect(LogType.Warning, new Regex("dropped 1 logs"));
            }

            LogBurst(3);
            MainLoop.Pump();
            LogBurst(3);
            MainLoop.Pump();

            Assert.That(sink.Messages, Has.Count.EqualTo(4));
        }

        /// <summary>
        /// The limit holds under concurrent producers.
        /// </summary>
        [Test]
        public void QueueLimit_HoldsUnderConcurrentWriters()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink(queueLimit: 100));
            LogAssert.Expect(LogType.Warning, new Regex("queue limit of 100"));
            LogAssert.Expect(LogType.Warning, new Regex("dropped 300 logs"));

            RunThreads(4, _ =>
            {
                for (int i = 0; i < 100; i++)
                {
                    Log.Info("m");
                }
            });
            MainLoop.Pump();

            Assert.That(sink.Messages, Has.Count.EqualTo(100));
        }

        private static string[] Expected(int count) =>
            Enumerable.Range(0, count).Select(i => $"[Info] [UnitySafeSinkTests]: {i}").ToArray();

        private static void LogBurst(int count)
        {
            RunThreads(1, _ =>
            {
                for (int i = 0; i < count; i++)
                {
                    Log.Info(i);
                }
            });
        }
    }
}

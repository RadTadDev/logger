using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
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
        [Category(KnownBug)]
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
        /// A failing delivery does not cost the rest of the batch (audit #4).
        /// </summary>
        [Test]
        [Category(KnownBug)]
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
        [Category(KnownBug)]
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
        [Category(KnownBug)]
        public void Drain_LargeBacklog_DoesNotStallMainThread()
        {
            const int messages = 10000;
            const long slowWriteTicksPerMicrosecond = 50;

            RecordingUnitySink sink = Add(new RecordingUnitySink());

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
    }
}

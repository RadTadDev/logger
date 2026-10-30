using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RadTadDev.Logger.Tests
{
    /// <summary>
    /// Registering sinks, and the <see cref="ThreadSafeSink"/> and <see cref="ConsoleSink"/> contracts.
    /// </summary>
    public class SinkTests : LoggerFixture
    {
        /// <summary>
        /// Every sink receives each message.
        /// </summary>
        [Test]
        public void AddSink_AllSinksReceiveMessages()
        {
            RecordingSink first = Add(new RecordingSink());
            RecordingSink second = Add(new RecordingSink());

            Log.Info("m");

            Assert.That(first.Messages, Has.Count.EqualTo(1));
            Assert.That(second.Messages, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Adding a sink twice does not duplicate its output.
        /// </summary>
        [Test]
        public void AddSink_Twice_RegistersOnce()
        {
            RecordingSink sink = Add(new RecordingSink());
            Log.AddSink(sink);

            Log.Info("m");

            Assert.That(sink.Messages, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Sinks are written in the order they were added.
        /// </summary>
        [Test]
        public void AddSink_PreservesRegistrationOrder()
        {
            var sinks = Enumerable.Range(0, 3).Select(_ => Add(new RecordingSink())).ToArray();

            Assert.That(Internals.Sinks, Is.EqualTo(sinks));
        }

        /// <summary>
        /// A null sink is refused, either loudly or by ignoring it, rather than failing every later log
        /// (audit #9).
        /// </summary>
        [Test]
        [Category(KnownBug)]
        public void AddSink_Null_DoesNotBreakLaterLogging()
        {
            RecordingSink sink = Add(new RecordingSink());

            try
            {
                Log.AddSink(null);
            }
            catch (ArgumentNullException)
            {
                // Refusing loudly is fine.
            }

            Log.Info("m");

            Assert.That(Internals.Sinks, Is.EqualTo(new[] { sink }));
        }

        /// <summary>
        /// Removing a sink stops its messages and closes it.
        /// </summary>
        [Test]
        public void RemoveSink_StopsMessagesAndCloses()
        {
            RecordingSink sink = Add(new RecordingSink());

            Log.RemoveSink(sink);
            Log.Info("m");

            Assert.That(sink.Messages, Is.Empty);
            Assert.That(sink.CloseCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Removing keeps the others, in order, from any position.
        /// </summary>
        /// <param name="index">The position to remove.</param>
        [Test]
        public void RemoveSink_KeepsTheRest([Values(0, 1, 2)] int index)
        {
            var sinks = Enumerable.Range(0, 3).Select(_ => Add(new RecordingSink())).ToList();
            RecordingSink removed = sinks[index];

            Log.RemoveSink(removed);
            sinks.RemoveAt(index);

            Assert.That(Internals.Sinks, Is.EqualTo(sinks));
        }

        /// <summary>
        /// A sink that was never added is neither closed nor an error.
        /// </summary>
        [Test]
        public void RemoveSink_Unregistered_IsIgnoredAndNotClosed()
        {
            var sink = new RecordingSink();

            Log.RemoveSink(sink);

            Assert.That(sink.CloseCount, Is.Zero);
        }

        /// <summary>
        /// Removing null is a no-op.
        /// </summary>
        [Test]
        public void RemoveSink_Null_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Log.RemoveSink(null));
        }

        /// <summary>
        /// Removing twice closes once.
        /// </summary>
        [Test]
        public void RemoveSink_Twice_ClosesOnce()
        {
            RecordingSink sink = Add(new RecordingSink());

            Log.RemoveSink(sink);
            Log.RemoveSink(sink);

            Assert.That(sink.CloseCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Re-adding a removed sink either works or is refused loudly, never silently dead (audit #10).
        /// </summary>
        [Test]
        [Category(KnownBug)]
        public void AddSink_AfterRemove_WorksOrRefuses()
        {
            RecordingSink sink = Add(new RecordingSink());
            Log.RemoveSink(sink);

            try
            {
                Log.AddSink(sink);
            }
            catch (InvalidOperationException)
            {
                Assert.Pass("Refused loudly.");
            }

            Log.Info("m");

            Assert.That(sink.Messages, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Close is idempotent.
        /// </summary>
        [Test]
        public void Close_Twice_NotifiesOnce()
        {
            var sink = new RecordingSink();

            sink.Close();
            sink.Close();

            Assert.That(sink.CloseCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Writes to a closed sink are dropped.
        /// </summary>
        [Test]
        public void Write_AfterClose_IsDropped()
        {
            var sink = new RecordingSink();

            sink.Close();
            sink.Write("m", LogLevel.Info);

            Assert.That(sink.Messages, Is.Empty);
        }

        /// <summary>
        /// One failing sink does not stop the next one, and the failure is reported.
        /// </summary>
        [Test]
        public void Dispatch_ThrowingSink_DoesNotStopOthers()
        {
            Add(new ThrowingSink { ThrowOnWrite = true });
            RecordingSink after = Add(new RecordingSink());
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("sink failure"));

            Assert.DoesNotThrow(() => Log.Info("m"));

            Assert.That(after.Messages, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Trace and Info log, Warn warns, Error errors.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <param name="type">The console type it should map to.</param>
        [TestCase(LogLevel.Trace, LogType.Log)]
        [TestCase(LogLevel.Info, LogType.Log)]
        [TestCase(LogLevel.Warn, LogType.Warning)]
        [TestCase(LogLevel.Error, LogType.Error)]
        public void ConsoleSink_MapsLevelToLogType(LogLevel level, LogType type)
        {
            Add(new ConsoleSink());
            Log.MinimumLevel = LogLevel.Trace;
            LogAssert.Expect(type, $"[{level}] [TestSupport]: mapped");

            LogAt(level, "mapped");
        }

        /// <summary>
        /// A level outside the enum is an error, not silence.
        /// </summary>
        [Test]
        public void ConsoleSink_UnknownLevel_Throws()
        {
            ConsoleSink sink = Add(new ConsoleSink());

            Assert.Throws<ArgumentOutOfRangeException>(() => sink.Write("m", (LogLevel)99));
        }
    }
}

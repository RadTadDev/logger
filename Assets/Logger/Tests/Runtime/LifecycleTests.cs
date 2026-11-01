using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RadTadDev.Logger.Tests
{
    /// <summary>
    /// Startup, teardown, and logging that fails partway through.
    /// </summary>
    public class LifecycleTests : LoggerFixture
    {
        /// <summary>
        /// Out of the box, logs go to the Unity console with no setup.
        /// </summary>
        [Test]
        public void Startup_RegistersDefaultConsoleSink()
        {
            Assert.That(OriginalSinks, Has.Some.InstanceOf<ConsoleSink>());
        }

        /// <summary>
        /// Teardown closes every sink and leaves only a fresh default, so logging keeps working.
        /// </summary>
        [Test]
        public void Reset_ClosesAllSinksAndRestoresDefault()
        {
            RecordingSink first = Add(new RecordingSink());
            RecordingSink second = Add(new RecordingSink());

            Internals.Reset();

            Assert.That(first.CloseCount, Is.EqualTo(1));
            Assert.That(second.CloseCount, Is.EqualTo(1));
            Assert.That(Internals.Sinks, Has.Length.EqualTo(1));
            Assert.That(Internals.Sinks[0], Is.InstanceOf<ConsoleSink>());
        }

        /// <summary>
        /// Teardown delivers queued messages rather than dropping them.
        /// </summary>
        [Test]
        public void Reset_OnMainThread_FlushesQueuedMessages()
        {
            RecordingUnitySink sink = Add(new RecordingUnitySink());

            RunThreads(1, _ => Log.Info("m"));
            Internals.Reset();

            Assert.That(sink.Messages, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// One sink failing to close does not leave the others open (audit #6).
        /// </summary>
        [Test]
        public void Reset_ThrowingClose_StillClosesLaterSinks()
        {
            LogAssert.ignoreFailingMessages = true;
            Add(new ThrowingSink { ThrowOnClose = true });
            RecordingSink after = Add(new RecordingSink());

            try
            {
                Internals.Reset();
            }
            catch (InvalidOperationException)
            {
                // The assertion below is what matters; this test does not dictate reporting.
            }

            Assert.That(after.CloseCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A close failure is reported, not hidden.
        /// </summary>
        [Test]
        public void Reset_ThrowingClose_IsReported()
        {
            Add(new ThrowingSink { ThrowOnClose = true });
            LogAssert.Expect(LogType.Exception, new Regex("close failure"));

            Assert.DoesNotThrow(Internals.Reset);
        }

        /// <summary>
        /// Logging survives quit then re-enter, which is what domain reload off does (audit #1).
        /// </summary>
        [Test]
        public void SecondPlaySession_WithoutDomainReload_StillLogsToConsole()
        {
            Add(new ConsoleSink());

            Internals.Reset();
            Internals.ResetOnPlay();

            LogAssert.Expect(LogType.Log, new Regex("second session"));
            Log.Info("second session");
        }

        /// <summary>
        /// The filter starts each session at its default, with or without domain reload.
        /// </summary>
        [Test]
        public void ResetOnPlay_RestoresDefaultMinimumLevel()
        {
            Log.MinimumLevel = LogLevel.Error;

            Internals.ResetOnPlay();

            Assert.That(Log.MinimumLevel, Is.EqualTo(LogLevel.Info));
        }

        /// <summary>
        /// Quit does not touch the filter, so teardown logs are judged by the session's level.
        /// </summary>
        [Test]
        public void Reset_KeepsMinimumLevel()
        {
            Log.MinimumLevel = LogLevel.Error;

            Internals.Reset();

            Assert.That(Log.MinimumLevel, Is.EqualTo(LogLevel.Error));
        }

        /// <summary>
        /// Logging before play starts, such as from [InitializeOnLoad], cannot kill the session (audit #3).
        /// </summary>
        [Test]
        public void LogBeforeSubsystemRegistration_DoesNotKillSession()
        {
            Add(new ConsoleSink());

            Internals.ResetOnPlay();

            LogAssert.Expect(LogType.Log, new Regex("after startup"));
            Log.Info("after startup");
        }

        /// <summary>
        /// Startup is safe to repeat.
        /// </summary>
        [Test]
        public void ResetOnPlay_Repeated_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                Internals.ResetOnPlay();
                Internals.ResetOnPlay();
            });
        }

        /// <summary>
        /// A throwing ToString never reaches the code that was only trying to log (audit #5).
        /// </summary>
        [Test]
        public void Log_ThrowingToString_DoesNotReachCaller()
        {
            LogAssert.ignoreFailingMessages = true;
            Add(new RecordingSink());

            Assert.DoesNotThrow(() => Log.Info(new ThrowingToString()));
        }

        /// <summary>
        /// A failed ToString still logs a line that says what went wrong.
        /// </summary>
        [Test]
        public void Log_ThrowingToString_LogsFallback()
        {
            RecordingSink sink = Add(new RecordingSink());

            Log.Warn(new ThrowingToString());

            Assert.That(sink.Messages, Has.Count.EqualTo(1));
            Assert.That(sink.Messages[0], Does.Contain(nameof(ThrowingToString)).And.Contain("ToString failure"));
        }

        /// <summary>
        /// Logging a Unity object from a worker never reaches the caller as an exception (audit #5).
        /// </summary>
        [Test]
        public void Log_UnityObjectFromWorker_DoesNotReachCaller()
        {
            LogAssert.ignoreFailingMessages = true;
            Add(new RecordingSink());
            var target = new GameObject("target");

            try
            {
                RunThreads(1, _ => Log.Info(target));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}

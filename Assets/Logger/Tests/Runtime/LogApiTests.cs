using System;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RadTadDev.Logger.Tests
{
    /// <summary>
    /// The public <see cref="Log" /> API: levels, filtering and message formatting.
    /// </summary>
    public class LogApiTests : LoggerFixture
    {
        /// <summary>
        /// Every combination of minimum level and message level filters correctly.
        /// </summary>
        /// <param name="minimum">The minimum level.</param>
        /// <param name="level">The message's level.</param>
        [Test]
        public void MinimumLevel_FiltersBelowAndKeepsAtOrAbove(
            [Values] LogLevel minimum,
            [Values(LogLevel.Trace, LogLevel.Info, LogLevel.Warn, LogLevel.Error)]
            LogLevel level)
        {
            var sink = Add(new RecordingSink());
            Log.MinimumLevel = minimum;

            LogAt(level, "message");

            Assert.That(sink.Messages.Count, Is.EqualTo(level >= minimum ? 1 : 0));
        }

        /// <summary>
        /// Each API reports its own level to sinks and in the message.
        /// </summary>
        /// <param name="level">The level.</param>
        [Test]
        public void EachApi_ReportsItsLevel(
            [Values(LogLevel.Trace, LogLevel.Info, LogLevel.Warn, LogLevel.Error)] LogLevel level)
        {
            var sink = Add(new RecordingSink());
            Log.MinimumLevel = LogLevel.Trace;

            LogAt(level, "message");

            Assert.That(sink.Levels, Is.EqualTo(new[] { level }));
            Assert.That(sink.Messages[0], Does.StartWith($"[{level}] "));
        }

        /// <summary>
        /// IsEnabled agrees with what the filter does.
        /// </summary>
        /// <param name="minimum">The minimum level.</param>
        /// <param name="level">The level asked about.</param>
        [Test]
        public void IsEnabled_MatchesFilter(
            [Values] LogLevel minimum,
            [Values(LogLevel.Trace, LogLevel.Info, LogLevel.Warn, LogLevel.Error)]
            LogLevel level)
        {
            Log.MinimumLevel = minimum;

            Assert.That(Log.IsEnabled(level), Is.EqualTo(level >= minimum));
        }

        /// <summary>
        /// None is a filter setting, never an enabled message level.
        /// </summary>
        /// <param name="minimum">The minimum level.</param>
        [Test]
        public void IsEnabled_None_IsAlwaysFalse([Values] LogLevel minimum)
        {
            Log.MinimumLevel = minimum;

            Assert.That(Log.IsEnabled(LogLevel.None), Is.False);
        }

        /// <summary>
        /// Trace is hidden at Info, the level the logger ships with.
        /// </summary>
        [Test]
        public void Trace_IsFilteredAtTheFixtureDefaultOfInfo()
        {
            var sink = Add(new RecordingSink());

            Log.Trace("hidden");

            Assert.That(sink.Messages, Is.Empty);
        }

        /// <summary>
        /// Messages are formatted as [Level] [CallerFile]: message.
        /// </summary>
        [Test]
        public void Message_IncludesLevelCallerFileAndText()
        {
            var sink = Add(new RecordingSink());

            Log.Info("hello");

            Assert.That(sink.Messages[0], Is.EqualTo("[Info] [LogApiTests]: hello"));
        }

        /// <summary>
        /// The caller's path is reduced to a bare file name.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="expected">The expected name.</param>
        [TestCase("/a/b/File.cs", "File")]
        [TestCase(@"C:\a\b\File.cs", "File")]
        [TestCase(@"C:\a/b\File.cs", "File")]
        [TestCase("File.cs", "File")]
        [TestCase("File", "File")]
        [TestCase("/a/b/File.Part.cs", "File.Part")]
        [TestCase("/a.b/File", "File")]
        [TestCase("/a/.hidden", ".hidden")]
        [TestCase("/a/b/", "")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void FilePath_IsReducedToFileName(string path, string expected)
        {
            var sink = Add(new RecordingSink());

            Log.Info("m", filePath: path);

            Assert.That(sink.Messages[0], Is.EqualTo($"[Info] [{expected}]: m"));
        }

        /// <summary>
        /// A null message is logged, not thrown on.
        /// </summary>
        [Test]
        public void NullMessage_IsLogged()
        {
            var sink = Add(new RecordingSink());

            Log.Info(null);

            Assert.That(sink.Messages, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Non-string messages are converted with ToString.
        /// </summary>
        [Test]
        public void ObjectMessage_UsesToString()
        {
            var sink = Add(new RecordingSink());
            var value = new Vector3(1, 2, 3);

            Log.Info(value);

            Assert.That(sink.Messages[0], Does.EndWith(value.ToString()));
        }

        /// <summary>
        /// Exceptions log at Error with their type and text.
        /// </summary>
        [Test]
        public void Exception_LogsAtErrorWithDetails()
        {
            var sink = Add(new RecordingSink());

            Log.Exception(new InvalidOperationException("broken"));

            Assert.That(sink.Levels, Is.EqualTo(new[] { LogLevel.Error }));
            Assert.That(sink.Messages[0], Does.Contain("InvalidOperationException").And.Contain("broken"));
        }

        /// <summary>
        /// A thrown exception's stack trace reaches the sink.
        /// </summary>
        [Test]
        public void Exception_IncludesStackTrace()
        {
            var sink = Add(new RecordingSink());
            Exception thrown = null;

            try
            {
                throw new InvalidOperationException("broken");
            }
            catch (InvalidOperationException e)
            {
                thrown = e;
            }

            Log.Exception(thrown);

            Assert.That(sink.Messages[0], Does.Contain(nameof(Exception_IncludesStackTrace)));
        }

        /// <summary>
        /// Exceptions are subject to the same filter as errors, which always pass.
        /// </summary>
        [Test]
        public void Exception_PassesTheHighestFilter()
        {
            var sink = Add(new RecordingSink());
            Log.MinimumLevel = LogLevel.Error;

            Log.Exception(new InvalidOperationException());

            Assert.That(sink.Messages, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// A null exception does not throw.
        /// </summary>
        [Test]
        public void Exception_Null_IsLogged()
        {
            var sink = Add(new RecordingSink());

            Log.Exception(null);

            Assert.That(sink.Messages, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Debug goes straight to the console, bypassing sinks.
        /// </summary>
        [Test]
        public void Debug_BypassesSinks()
        {
            var sink = Add(new RecordingSink());
            LogAssert.Expect(LogType.Log, new Regex(@"\[DEBUG\].*temporary"));

            Log.Debug("temporary");

            Assert.That(sink.Messages, Is.Empty);
        }

        /// <summary>
        /// Debug is a quick tool, so the level filter does not hide it.
        /// </summary>
        [Test]
        public void Debug_IgnoresMinimumLevel()
        {
            Log.MinimumLevel = LogLevel.Error;
            LogAssert.Expect(LogType.Log, new Regex("still visible"));

            Log.Debug("still visible");
        }

        /// <summary>
        /// Debug is a logging call too, so a throwing ToString stays out of the caller.
        /// </summary>
        [Test]
        public void Debug_ThrowingToString_DoesNotReachCaller()
        {
            LogAssert.Expect(LogType.Log, new Regex("ToString failure"));

            Assert.DoesNotThrow(() => Log.Debug(new ThrowingToString()));
        }

        /// <summary>
        /// Logging with no sinks at all is a harmless no-op.
        /// </summary>
        [Test]
        public void NoSinks_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Log.Error("nobody listening"));
        }

        /// <summary>
        /// Logger frames stay out of Console callstacks so double-clicking lands on the caller.
        /// </summary>
        /// <param name="method">The method.</param>
        [TestCase(nameof(Log.Trace))]
        [TestCase(nameof(Log.Info))]
        [TestCase(nameof(Log.Warn))]
        [TestCase(nameof(Log.Error))]
        [TestCase(nameof(Log.Exception))]
        [TestCase(nameof(Log.Debug))]
        public void PublicLoggingMethods_HideInCallstack(string method)
        {
            var info = typeof(Log).GetMethod(method, BindingFlags.Public | BindingFlags.Static);

            Assert.That(info.GetCustomAttribute<HideInCallstackAttribute>(), Is.Not.Null);
        }

        /// <summary>
        /// The private frame between the public API and the sinks is hidden too (audit: low).
        /// </summary>
        [Test]
        public void PrivateDispatch_HidesInCallstack()
        {
            var info = typeof(Log).GetMethod("Dispatch", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(info.GetCustomAttribute<HideInCallstackAttribute>(), Is.Not.Null);
        }
    }
}

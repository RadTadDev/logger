using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RadTadDev.Logger.Editor.Tests
{
    /// <summary>
    /// Logging outside play mode, where no runtime initialization has run.
    /// These tests use the real default sink rather than isolating it, because that is the subject.
    /// </summary>
    public class EditModeLoggingTests
    {
        private sealed class CollectingSink : ThreadSafeSink
        {
            /// <summary>
            /// Messages received.
            /// </summary>
            public List<string> Messages { get; } = new();

            /// <inheritdoc/>
            protected override void WriteSerial(string message, LogLevel level) => Messages.Add(message);

            /// <inheritdoc/>
            protected override void OnClosed() { }
        }

        /// <summary>
        /// Editor scripts can log from the main thread (audit #2).
        /// </summary>
        [Test]
        public void MainThreadLog_ReachesConsole()
        {
            LogAssert.Expect(LogType.Log, new Regex("edit mode main"));

            Log.Info("edit mode main");
        }

        /// <summary>
        /// Editor scripts can log from worker threads (audit #2). The test runner does not pump the
        /// synchronization context mid-test, so a main thread log does the flushing here.
        /// </summary>
        [Test]
        public void WorkerLog_ReachesConsole()
        {
            LogAssert.Expect(LogType.Log, new Regex("edit mode worker"));
            LogAssert.Expect(LogType.Log, new Regex("edit mode flush"));

            Task.Run(() => Log.Info("edit mode worker")).Wait();
            Log.Info("edit mode flush");
        }

        /// <summary>
        /// Plain thread safe sinks need no main thread and work in edit mode.
        /// </summary>
        [Test]
        public void ThreadSafeSink_WorksInEditMode()
        {
            var sink = new CollectingSink();
            Log.AddSink(sink);

            try
            {
                Log.Info("m");
            }
            finally
            {
                Log.RemoveSink(sink);
            }

            Assert.That(sink.Messages, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// The quick debug path works in edit mode.
        /// </summary>
        [Test]
        public void Debug_WorksInEditMode()
        {
            LogAssert.Expect(LogType.Log, new Regex("edit debug"));

            Log.Debug("edit debug");
        }
    }
}

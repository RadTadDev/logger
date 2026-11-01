using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RadTadDev.Logger.Tests
{
    /// <summary>
    /// End to end checks against Unity's real main loop and synchronization context.
    /// </summary>
    public class MainLoopIntegrationTests : LoggerFixture
    {
        private const int MaxFrames = 300;

        /// <inheritdoc />
        protected override bool UsePumpContext => false;

        /// <summary>
        /// Worker logs reach the sink within a few frames with nothing pumping by hand.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator WorkerLog_ArrivesOnALaterFrame()
        {
            var sink = Add(new RecordingUnitySink());
            var task = Task.Run(() => Log.Info("m"));

            for (var i = 0; i < MaxFrames && (!task.IsCompleted || sink.Messages.Count == 0); i++)
            {
                yield return null;
            }

            Assert.That(sink.Messages, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// The real console receives worker logs.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator WorkerLog_ReachesConsole()
        {
            Add(new ConsoleSink());
            LogAssert.Expect(LogType.Warning, "[Warn] [MainLoopIntegrationTests]: from worker");

            var task = Task.Run(() => Log.Warn("from worker"));

            for (var i = 0; i < 10 || !task.IsCompleted; i++)
            {
                yield return null;
            }
        }

        /// <summary>
        /// A drain that was posted while frames pass keeps working across several batches.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator WorkerLogs_AcrossFrames_AllArrive()
        {
            const int batches = 5;
            const int perBatch = 100;

            var sink = Add(new RecordingUnitySink());

            for (var batch = 0; batch < batches; batch++)
            {
                Parallel.For(0, perBatch, i => Log.Info(i));
                yield return null;
            }

            for (var i = 0; i < MaxFrames && sink.Messages.Count < batches * perBatch; i++)
            {
                yield return null;
            }

            Assert.That(sink.Messages, Has.Count.EqualTo(batches * perBatch));
        }
    }
}

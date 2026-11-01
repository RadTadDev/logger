using System;
using UnityEngine;

namespace RadTadDev.Logger
{
    /// <summary>
    /// A default sink that logs to the Unity console.
    /// </summary>
    public class ConsoleSink : UnitySafeSink
    {
        /// <summary>
        /// Creates a new console sink.
        /// </summary>
        /// <param name="drainPerFrameLimit">Maximum number of logs to print in a single frame.</param>
        /// <param name="queueLimit">The maximum number of logs to queue at any given time.</param>
        public ConsoleSink(int drainPerFrameLimit = DefaultDrainPerFrameLimit, int queueLimit = DefaultQueueLimit)
            : base(drainPerFrameLimit, queueLimit) { }

        /// <inheritdoc />
        [HideInCallstack]
        protected override void WriteOnMainThread(string message, LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Info:
                    Debug.Log(message);
                    break;
                case LogLevel.Warn:
                    Debug.LogWarning(message);
                    break;
                case LogLevel.Error:
                    Debug.LogError(message);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }
        }
    }
}

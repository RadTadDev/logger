using System;
using UnityEngine;

namespace RadTadDev.Logger
{
    /// <summary>
    /// A default sink that logs to the Unity console.
    /// </summary>
    public class ConsoleSink : UnitySafeSink
    {
        /// <inheritdoc/>
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

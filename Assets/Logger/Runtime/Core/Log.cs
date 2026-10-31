using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace RadTadDev.Logger
{
    /// <summary>
    /// Creates logger objects.
    /// </summary>
    public static class Log
    {
        /// <summary>
        /// The minimum level of message to log.
        /// </summary>
        /// <remarks>Defaults to <see cref="LogLevel.Info"/></remarks>
        public static LogLevel MinimumLevel = LogLevel.Info;

        /// <summary>
        /// Logs verbose information.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <remarks>Note: It is intended that only the <see cref="message"/> parameter is used in this API.</remarks>
        [HideInCallstack]
        public static void Trace
        (
            object message,
            LogParamSentinel _ = null,
            [CallerFilePath] string filePath = null
        ) => Dispatch(message, filePath, LogLevel.Trace);

        /// <summary>
        /// Logs information.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <remarks>Note: It is intended that only the <see cref="message"/> parameter is used in this API.</remarks>
        [HideInCallstack]
        public static void Info
        (
            object message,
            LogParamSentinel _ = null,
            [CallerFilePath] string filePath = null
        ) => Dispatch(message, filePath, LogLevel.Info);

        /// <summary>
        /// Logs a warning.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <remarks>Note: It is intended that only the <see cref="message"/> parameter is used in this API.</remarks>
        [HideInCallstack]
        public static void Warn(
            object message,
            LogParamSentinel _ = null,
            [CallerFilePath] string filePath = null
        ) => Dispatch(message, filePath, LogLevel.Warn);

        /// <summary>
        /// Logs an error.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <remarks>Note: It is intended that only the <see cref="message"/> parameter is used in this API.</remarks>
        [HideInCallstack]
        public static void Error(
            object message,
            LogParamSentinel _ = null,
            [CallerFilePath] string filePath = null
        ) => Dispatch(message, filePath, LogLevel.Error);

        /// <summary>
        /// Logs an exception.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <remarks>Note: It is intended that only the <see cref="exception"/> parameter is used in this API.</remarks>
        [HideInCallstack]
        public static void Exception
        (
            Exception exception,
            LogParamSentinel _ = null,
            [CallerFilePath] string filePath = null
        ) => Dispatch(exception, filePath, LogLevel.Error);

        /// <summary>
        /// Log a quick, temporary message to the Unity console.
        /// </summary>
        /// <param name="message">The message.</param>
        [HideInCallstack]
        public static void Debug(object message)
        {
            UnityEngine.Debug.Log($"<color=#2E9BFF>======= [DEBUG]:</color> {message}");
        }

        /// <summary>
        /// Adds a sink to send every message to.
        /// </summary>
        /// <param name="sink">The sink to add.</param>
        /// <exception cref="ArgumentNullException"><paramref name="sink"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="sink"/> was closed, which cannot be undone.</exception>
        public static void AddSink(ISink sink) => SinkManager.AddSink(sink);

        /// <summary>
        /// Removes a sink so it receives no more messages.
        /// </summary>
        /// <param name="sink">The sink to remove.</param>
        /// <remarks>
        /// Sinks are cleaned up automatically both when the player application enters and exits, thus
        /// there is no need to call this on application teardown.
        /// </remarks>
        public static void RemoveSink(ISink sink) => SinkManager.RemoveSink(sink);

        [HideInCallstack]
        private static void Dispatch(object message, string filePath, LogLevel level)
        {
            if (level >= MinimumLevel)
            {
                SinkManager.Dispatch($"[{level}] [{GetFileName(filePath)}]: {Format(message)}", level);
            }
        }

        private static string Format(object message)
        {
            try
            {
                return $"{message}";
            }
            catch (Exception e)
            {
                // Logging must never throw into the caller, e.g. Unity objects off the main thread.
                return $"<{message.GetType().Name}.ToString threw {e.GetType().Name}: {e.Message}>";
            }
        }

        private static string GetFileName(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return string.Empty;
            }

            // -1 + 1 = 0 if no separator
            int start = filePath.LastIndexOfAny(new[] { '/', '\\' }) + 1;
            int end = filePath.Length;

            int dot = filePath.LastIndexOf('.');
            if (dot > start)
            {
                end = dot;
            }

            return filePath.Substring(start, end - start);
        }
    }
}

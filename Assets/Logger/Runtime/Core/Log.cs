using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace RadTadDev.Logger
{
    /// <summary>
    /// The entry point for writing logs and managing sinks.
    /// </summary>
    public static class Log
    {
        internal const LogLevel DefaultMinimumLevel = LogLevel.Info;

        private static readonly char[] PathSeparators = { '/', '\\' };

        /// <summary>
        /// The minimum level of message to log. Set to <see cref="LogLevel.None"/> to log nothing.
        /// </summary>
        /// <remarks>Defaults to <see cref="LogLevel.Info"/>, and returns to it whenever play mode starts.</remarks>
        public static volatile LogLevel MinimumLevel = DefaultMinimumLevel;

        /// <summary>
        /// Logs verbose information.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <remarks>Note: It is intended that only the <paramref name="message"/> parameter is used in this API.</remarks>
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
        /// <remarks>Note: It is intended that only the <paramref name="message"/> parameter is used in this API.</remarks>
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
        /// <remarks>Note: It is intended that only the <paramref name="message"/> parameter is used in this API.</remarks>
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
        /// <remarks>Note: It is intended that only the <paramref name="message"/> parameter is used in this API.</remarks>
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
        /// <remarks>Note: It is intended that only the <paramref name="exception"/> parameter is used in this API.</remarks>
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
            UnityEngine.Debug.Log($"<color=#2E9BFF>======= [DEBUG]:</color> {Format(message)}");
        }

        /// <summary>
        /// Whether a message at this level would be logged. Use it to skip building expensive messages.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <returns>True if messages at <paramref name="level"/> reach the sinks.</returns>
        public static bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= MinimumLevel;

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
        /// there is no need to call this on application teardown. That cleanup runs on
        /// <see cref="Application.quitting"/>, so logs from <c>OnDisable</c> and <c>OnDestroy</c> during
        /// quit reach only a fresh <see cref="ConsoleSink"/>.
        /// </remarks>
        public static void RemoveSink(ISink sink) => SinkManager.RemoveSink(sink);

        /// <summary>
        /// Removes and closes every sink, including the default <see cref="ConsoleSink"/>. Use this
        /// before adding sinks to replace the default rather than add to it.
        /// </summary>
        public static void ClearSinks() => SinkManager.ClearSinks();

        [HideInCallstack]
        private static void Dispatch(object message, string filePath, LogLevel level)
        {
            if (IsEnabled(level))
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
            int start = filePath.LastIndexOfAny(PathSeparators) + 1;
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

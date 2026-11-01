using System;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace RadTadDev.Logger
{
    /// <summary>
    /// The <see cref="SinkManager" /> handles collecting and dispatching to sinks.
    /// </summary>
    internal static class SinkManager
    {
        private static ISink[] _sinks = { new ConsoleSink() };
        private static readonly object Lock = new();

        /// <summary>
        /// Add a sink to receive log messages.
        /// </summary>
        /// <param name="sink">The sink.</param>
        public static void AddSink(ISink sink)
        {
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            // Checked outside Lock: a sink can hold its own lock while calling back into the logger.
            if (sink is ThreadSafeSink { IsClosed: true })
            {
                throw new InvalidOperationException($"Cannot add a closed {sink.GetType().Name}.");
            }

            lock (Lock)
            {
                if (_sinks.Any(s => s == sink))
                {
                    return;
                }

                var next = new ISink[_sinks.Length + 1];

                _sinks.CopyTo(next, 0);
                next[^1] = sink;

                Volatile.Write(ref _sinks, next);
            }
        }

        /// <summary>
        /// Remove a sink.
        /// </summary>
        /// <param name="sink">The sink.</param>
        public static void RemoveSink(ISink sink)
        {
            lock (Lock)
            {
                var index = -1;
                for (var i = 0; i < _sinks.Length; i++)
                {
                    if (_sinks[i] == sink)
                    {
                        index = i;
                        break;
                    }
                }

                if (index == -1)
                {
                    return;
                }

                var next = new ISink[_sinks.Length - 1];
                var j = 0;

                for (var i = 0; i < _sinks.Length; i++)
                {
                    if (i == index)
                    {
                        continue;
                    }

                    next[j++] = _sinks[i];
                }

                Volatile.Write(ref _sinks, next);
            }

            CloseSafely(sink);
        }

        /// <summary>
        /// Removes and closes every sink.
        /// </summary>
        public static void ClearSinks() => ReplaceSinks(Array.Empty<ISink>());

        /// <summary>
        /// Dispatches a message to all registered sinks.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="level">The level to log at.</param>
        [HideInCallstack]
        public static void Dispatch(string message, LogLevel level)
        {
            var sinks = Volatile.Read(ref _sinks);
            foreach (var t in sinks)
            {
                try
                {
                    t.Write(message, level);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            Reset();

            // Domain reload off keeps statics, so this matches what a reload would have done.
            Log.MinimumLevel = Log.DefaultMinimumLevel;

            Application.quitting -= Reset;
            Application.quitting += Reset;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void ResetBeforeAssemblyReload()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Reset;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Reset;
        }
#endif

        // Replaced rather than emptied: nothing reruns to restore it with domain reload off.
        private static void Reset() => ReplaceSinks(new ISink[] { new ConsoleSink() });

        private static void ReplaceSinks(ISink[] next)
        {
            ISink[] old;

            lock (Lock)
            {
                old = _sinks;
                Volatile.Write(ref _sinks, next);
            }

            foreach (var sink in old)
            {
                CloseSafely(sink);
            }
        }

        private static void CloseSafely(ISink sink)
        {
            try
            {
                sink.Close();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}

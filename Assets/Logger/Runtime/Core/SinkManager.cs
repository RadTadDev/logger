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
        private static ISink[] _sinks = Array.Empty<ISink>();
        private static readonly object Lock = new();

        /// <summary>
        /// Add a sink to receive log messages.
        /// </summary>
        /// <param name="sink">The sink.</param>
        public static void AddSink(ISink sink)
        {
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

            sink.Close();
        }

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

            Application.quitting -= Reset;
            Application.quitting += Reset;
        }

        private static void Reset()
        {
            ISink[] old;

            lock (Lock)
            {
                old = _sinks;
                Volatile.Write(ref _sinks, Array.Empty<ISink>());
            }

            foreach (var sink in old)
            {
                sink.Close();
            }
        }
    }
}

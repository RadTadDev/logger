using System.Threading;
using UnityEngine;

namespace RadTadDev.Logger
{
    /// <summary>
    /// A thread safe sink base.
    /// </summary>
    public abstract class ThreadSafeSink : ISink
    {
        private bool _closed;
        private readonly object _lock = new();

        /// <summary>
        /// Whether this sink has been closed. Closed sinks never reopen.
        /// </summary>
        internal bool IsClosed => Volatile.Read(ref _closed);

        /// <inheritdoc/>
        [HideInCallstack]
        public void Write(string message, LogLevel level)
        {
            lock (_lock)
            {
                if (!_closed)
                {
                    WriteSerial(message, level);
                }
            }
        }

        /// <inheritdoc/>
        public void Close()
        {
            lock (_lock)
            {
                if (_closed)
                {
                    return;
                }

                _closed = true;
                OnClosed();
            }
        }

        /// <summary>
        /// Writes a message to this sink. This call is serialized, so non-thread safe
        /// calls are safe from within it.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="level">The level to log at.</param>
        protected abstract void WriteSerial(string message, LogLevel level);

        /// <summary>
        /// Called when this sink is closed.
        /// </summary>
        protected abstract void OnClosed();
    }
}

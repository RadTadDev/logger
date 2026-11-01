namespace RadTadDev.Logger
{
    /// <summary>
    /// A destination for all log messages to appear at.
    /// </summary>
    public interface ISink
    {
        /// <summary>
        /// Writes a message to the sink.
        /// </summary>
        /// <param name="message">The message to write.</param>
        /// <param name="level">The level to log at.</param>
        public void Write(string message, LogLevel level);

        /// <summary>
        /// Closes this sink.
        /// </summary>
        public void Close();
    }
}

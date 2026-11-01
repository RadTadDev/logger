namespace RadTadDev.Logger
{
    /// <summary>
    /// The verbosity levels the loggers support.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// Most verbose.
        /// </summary>
        Trace,

        /// <summary>
        /// Information.
        /// </summary>
        Info,

        /// <summary>
        /// Warnings.
        /// </summary>
        Warn,

        /// <summary>
        /// Errors.
        /// </summary>
        Error,

        /// <summary>
        /// Logs nothing. Only meaningful for <see cref="Log.MinimumLevel" />; no message has this level.
        /// </summary>
        None
    }
}

using System.ComponentModel;

namespace RadTadDev.Logger
{
    /// <summary>
    /// A sentinel to prevent accidentally passing a value to a caller info parameter.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class LogParamSentinel
    {
        internal LogParamSentinel() { }
    }
}

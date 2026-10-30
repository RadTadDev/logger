using System.ComponentModel;

namespace RadTadDev.Logger
{
    /// <summary>
    /// A sentinel to prevent accidental
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class LogParamSentinel
    {
        internal LogParamSentinel() { }
    }
}

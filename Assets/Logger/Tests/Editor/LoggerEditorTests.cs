using NUnit.Framework;

namespace RadTadDev.Logger.Editor.Tests
{
    /// <summary>
    /// Editor tests for Logger.
    /// </summary>
    public class LoggerEditorTests
    {
        /// <summary>
        /// The editor assembly is referenced and loads.
        /// </summary>
        [Test]
        public void EditorAssemblyLoads()
        {
            Assert.IsNotNull(typeof(LoggerMenu));
        }
    }
}

using NUnit.Framework;

namespace RadTadDev.Logger.Tests
{
    /// <summary>
    /// Runtime tests for Logger.
    /// </summary>
    public class LoggerTests
    {
        /// <summary>
        /// The package name constant matches package.json.
        /// </summary>
        [Test]
        public void PackageNameIsSet()
        {
            Assert.AreEqual("com.radtaddev.logger", LoggerInfo.PackageName);
        }
    }
}

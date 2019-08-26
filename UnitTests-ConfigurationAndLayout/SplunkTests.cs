using System;
using System.Threading.Tasks;
using NLog;
using NUnit.Framework;

namespace UnitTests_ConfigurationAndLayout
{
    [TestFixture]
    public class SplunkTests
    {
        private ILogger _logger = null;
        [SetUp]
        public void TestSetUp()
        {
            _logger = LogManager.GetCurrentClassLogger();
        }

        [Test]
        public void LogTests()
        {
            _logger.Info($"{DateTime.Now} - Confirm visually " );
        }

        [Test]
        public void LoadTest()
        {
            Parallel.For(0, 100, i => _logger.Info($"{i} at {DateTime.Now}"));
        }

    }
}

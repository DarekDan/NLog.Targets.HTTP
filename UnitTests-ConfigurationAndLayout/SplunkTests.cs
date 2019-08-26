using System;
using System.Threading.Tasks;
using NLog;
using NUnit.Framework;

namespace UnitTests_ConfigurationAndLayout
{
    [TestFixture]
    public class SplunkTests
    {
        [SetUp]
        public void TestSetUp()
        {
            _logger = LogManager.GetCurrentClassLogger();
        }

        [TearDown]
        public void TestShutdown()
        {
            LogManager.Flush();
        }

        private ILogger _logger;

        [Test]
        public void LoadTest()
        {
            Parallel.For(0, 10, i => _logger.Info($"{i} at {DateTime.Now}"));
        }

        [Test]
        public void LogTests()
        {
            _logger.Info($"{DateTime.Now} - Confirm visually ");
        }
    }
}
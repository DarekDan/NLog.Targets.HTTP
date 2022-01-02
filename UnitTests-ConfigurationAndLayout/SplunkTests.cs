using System;
using System.Threading.Tasks;
using NLog;
using NLog.Common;
using NUnit.Framework;

namespace UnitTests_ConfigurationAndLayout
{
    [TestFixture(Ignore = "true")]
    public class SplunkTests
    {
        [OneTimeSetUp]
        public void TestSetUp()
        {
            _logger = LogManager.GetCurrentClassLogger();
        }

        [OneTimeTearDown]
        public void TestShutdown()
        {
            LogManager.Flush(TimeSpan.FromHours(1));
            LogManager.Shutdown();
        }

        private ILogger _logger;
        private readonly Guid _guid = Guid.NewGuid();

        [Test]
        public void LoadTest()
        {
            Parallel.For(0, 1000000, i => _logger.Info($"{_guid} {i} at {DateTime.Now}"));
            InternalLogger.Debug(_guid.ToString());
        }

        [Test]
        public void LogTests()
        {
            for (var i = 0; i < 5; i++) _logger.Warn($"{i} !!! {DateTime.Now} - Confirm visually ");
        }
    }
}
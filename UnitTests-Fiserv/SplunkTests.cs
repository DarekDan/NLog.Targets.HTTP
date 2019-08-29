using System;
using System.Threading.Tasks;
using NLog;
using NLog.Fluent;
using NUnit.Framework;

namespace UnitTests_Fiserv
{
    [TestFixture]
    public class SplunkTests
    {
        [OneTimeSetUp]
        public void TestSetUp()
        {
            LogManager.ThrowConfigExceptions = true;
            _logger = LogManager.GetCurrentClassLogger();
        }

        [OneTimeTearDown]
        public void TestShutdown()
        {
            LogManager.Flush();
            LogManager.Shutdown();
        }

        private ILogger _logger;

        [Test]
        public void LoadTest()
        {
            Parallel.For(0, 100000, i => _logger.Info()
                .Message($"Testing NLog: Message #{i} at {DateTime.Now}")
                .Property("seq", i % 100)
                .Property("interactionid", Guid.NewGuid().ToString()).Write());
        }

        [Test]
        public void LogTests()
        {
            for (var i = 0; i < 1; i++)
            {
                _logger.Warn()
                    .Message($"XX WARN {i} !!! {DateTime.Now} - Confirm visually ")
                    .Property("seq", i % 100)
                    .Property("interactionid", Guid.NewGuid().ToString())
                    .Write();
                _logger.Info()
                    .Message($"XX INFO {i} !!! {DateTime.Now} - Confirm visually ")
                    .Property("seq", i % 100)
                    .Property("interactionid", Guid.NewGuid().ToString()).Write();
                _logger.Debug()
                    .Message($"XX DEBUG {i} !!! {DateTime.Now} - Confirm visually ")
                    .Property("seq", i % 100)
                    .Property("interactionid", Guid.NewGuid().ToString())
                    .Write();
            }

            //LogManager.Flush();
        }
    }
}
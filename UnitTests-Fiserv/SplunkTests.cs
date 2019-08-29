using NLog;
using NLog.Fluent;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace Tests
{
    [TestFixture]
    public class SplunkTests
    {
        [OneTimeSetUp]
        public void TestSetUp()
        {
            try
            {
                LogManager.ThrowConfigExceptions = true;
                _logger = LogManager.GetCurrentClassLogger();
            }
            catch (Exception e)
            {
                var s = e.Message;
            }
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
            for (int i = 0; i < 1; i++)
            {
                _logger.Warn()
                    .Message($"XX WARN {i} !!! {DateTime.Now} - Confirm visually ")
                    .Property("seq", i % 100)
                    .Property("interactionid", Guid.NewGuid().ToString())
                    .Write();
                _logger.Info()
                    .Message($"XX INFO {i} !!! {DateTime.Now} - Confirm visually ")
                    .Property("seq", i % 100)
                    .Property("interactionid", Guid.NewGuid().ToString())    .Write();
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
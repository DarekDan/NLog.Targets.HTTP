using System.Threading.Tasks;
using System;
using NUnit.Framework;
using NLog;
using NLog.Fluent;

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
            Parallel.For(0, 1000, i => _logger.Info()
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
                    .Message($"{i} !!! {DateTime.Now} - Confirm visually ")
                    .Property("seq", i % 100)
                    .Property("interactionid", Guid.NewGuid().ToString())
                    .Write();
            }
            //LogManager.Flush();
        }
    }
}
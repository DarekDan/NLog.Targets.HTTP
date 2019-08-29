using System;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets.Http;
using NUnit.Framework;

namespace UnitTests_Coded
{
    [TestFixture]
    public class SplunkFiservTests
    {
        [SetUp]
        public void TestSetup()
        {
            var config = new LoggingConfiguration();
            var target = new HTTP
            {
                Url = "https://us-splunk-hec.1dc.com/services/collector/event",
                Authorization = "Splunk 1c8c9650-8c63-49f1-9968-edb95acc8773",
                Layout = new JsonLayout
                {
                    Attributes =
                    {
                        new JsonAttribute("sourcetype", "_json"),
                        new JsonAttribute("host", "TODO"),
                        new JsonAttribute("event", new JsonLayout
                            {
                                Attributes =
                                {
                                    new JsonAttribute("level", "${level:upperCase=true}"),
                                    new JsonAttribute("source", "${logger}"),
                                    new JsonAttribute("message", "${message}")
                                }
                            },
                            //don't escape layout
                            false)
                    }
                }
            };
            config.AddTarget("splunkTarget", target);

            // Step 4. Define rules
            var rule1 = new LoggingRule("*", LogLevel.Trace, target);
            config.LoggingRules.Add(rule1);

            // Step 5. Activate the configuration
            LogManager.Configuration = config;
            LogManager.ReconfigExistingLoggers();

            // Step 6. Create logger
            _logger = LogManager.GetCurrentClassLogger();
        }

        private ILogger _logger;

        [OneTimeTearDown]
        public void TestShutdown()
        {
            LogManager.Flush();
            LogManager.Shutdown();
        }

        [Test]
        public void LogTests()
        {
            _logger.Info($"{DateTime.Now} - Confirm visually ");
        }
    }
}
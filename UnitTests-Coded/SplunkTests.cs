using System;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets.Http;
using NUnit.Framework;

namespace UnitTests_Coded
{
    [TestFixture]
    public class SplunkTests
    {
        private ILogger _logger = null;

        [OneTimeTearDown]
        public void TestShutdown()
        {
            LogManager.Flush();
            LogManager.Shutdown();
        }

        [SetUp]
        public void TestSetup()
        {
            var config = new LoggingConfiguration();
            var target = new HTTP
            {
                URL = "https://input-prd-p-9dvvm7mz6x87.cloud.splunk.com:8088/services/collector",
                Authorization = "Splunk a575956e-10a5-4048-8b69-3f064da1ca88",
                Layout = new JsonLayout{Attributes =
                {
                    new JsonAttribute("sourcetype", "_json"),
                    //TODO
                    new JsonAttribute("host", "TODO"),
                    new JsonAttribute("event", new JsonLayout
                        {

                            Attributes =
                            {
                                new JsonAttribute("level", "${level:upperCase=true}"),
                                new JsonAttribute("source", "${logger}"),
                                new JsonAttribute("message", "${message}"),
                            }
                        },
                        //don't escape layout
                        false)
                }}
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

        [Test]
        public void LogTests()
        {
            _logger.Info($"{DateTime.Now} - Confirm visually " );
        }
    }
}

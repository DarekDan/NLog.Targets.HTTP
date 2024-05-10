using System;
using System.Diagnostics;
using System.Threading.Tasks;
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
        [SetUp]
        public void TestSetup()
        {
            var config = new LoggingConfiguration();
            var target = new HTTP
            {
                Url = "https://localhost:8088/services/collector/event",
                Authorization = "Splunk cd3f0725-5e56-440b-bafc-0ccb663537c1",
                Name = "SplunkTarget",
                InMemoryCompression = false,
                BatchSize = 50000,
                BatchAsJsonArray = false,
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
                },
                Headers = new[] { new NHttpHeader { Name = "N1", Value = "V1" } }
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

        [OneTimeTearDown]
        public void TestShutdown()
        {
            LogManager.Flush(TimeSpan.FromMinutes(1));
            LogManager.Shutdown();
        }

        private ILogger _logger;

        [Test]
        public void LogTests()
        {
            _logger.Info($"{DateTime.Now} - Confirm visually ");
        }

        [Test]
        public void LogPerformanceTest()
        {
            var sw = Stopwatch.StartNew();
            Parallel.For(0, 1000, i => { Parallel.For(0, 1000, j => { _logger.Info($"{i}:{j}"); }); });
            Console.WriteLine(sw.Elapsed);
            LogManager.Flush(TimeSpan.FromHours(1));
            Console.WriteLine(sw.Elapsed);
        }
    }
}
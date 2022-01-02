// Licensed Materials - Property of Quaterne
// 
// (C) Copyright Quaterne, LLC - 2019

using NLog;
using NLog.Targets.Http;
using NUnit.Framework;

// ReSharper disable NotAccessedField.Local

namespace UnitTests_ConfigurationAndLayout
{
    [TestFixture]
    public class ConfigurationUpdatesTests
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            _logger = LogManager.GetCurrentClassLogger();
            _testDirectory = TestContext.CurrentContext.TestDirectory;
        }

        private string _testDirectory;
        private Logger _logger;

        [Test]
        public void HeadersAreNotEmpty()
        {
            Assert.True(LogManager.Configuration.AllTargets.Count > 0);
            var splunkTarget = (HTTP)LogManager.Configuration.FindTargetByName("splunk");
            Assert.That(splunkTarget.Headers.Count == 2);
        }
    }
}
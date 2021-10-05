// Licensed Materials - Property of Quaterne
// 
// (C) Copyright Quaterne, LLC - 2019

using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using NLog;
using NLog.Config;
using NLog.Targets.Http;
using NUnit.Framework;

namespace UnitTests_ConfigurationAndLayout
{
    [TestFixture]
    public class ConfigurationUpdatesTests
    {
        private string _testDirectory;
        private Logger _logger;

        [OneTimeSetUp]
        public void SetUp()
        {
            _logger = LogManager.GetCurrentClassLogger();
            _testDirectory = TestContext.CurrentContext.TestDirectory;
        }

        [Test]
        public void HeadersAreNotEmpty()
        {
            Assert.True(LogManager.Configuration.AllTargets.Count>0);
            var splunkTarget = (HTTP) LogManager.Configuration.FindTargetByName("splunk");
            Assert.That(splunkTarget.Headers.Count==2);
        }

    }
}
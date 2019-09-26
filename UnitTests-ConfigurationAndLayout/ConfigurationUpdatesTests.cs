// Licensed Materials - Property of Quaterne
// 
// (C) Copyright Quaterne, LLC - 2019

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using NLog;
using NLog.Config;
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
        public void MakeChangeAndConfirm()
        {
            var semaphore = new SemaphoreSlim(1);
            semaphore.Wait();

            void LogManagerOnConfigurationReloaded(object sender, LoggingConfigurationReloadedEventArgs args)
            {
                semaphore.Release();
            }

            LogManager.ConfigurationReloaded += LogManagerOnConfigurationReloaded;

            var configFilePath = Path.Combine(_testDirectory, "nlog.config");
            var doc = XDocument.Load(configFilePath);
            XmlNameTable nameTable = new NameTable();
            var namespaceManager = new XmlNamespaceManager(nameTable);
            namespaceManager.AddNamespace("ns1", doc.Root.GetDefaultNamespace().NamespaceName);
            var target = doc.Root.XPathSelectElement("ns1:targets/ns1:target[@name='splunk']", namespaceManager);
            const int expectedConnectionLimit = 1000;
            target.Add(new XAttribute("DefaultConnectionLimit", expectedConnectionLimit));
            doc.Save(configFilePath);
            semaphore.Wait(TimeSpan.FromSeconds(5));
            Assert.AreEqual(expectedConnectionLimit, ServicePointManager.DefaultConnectionLimit);
            semaphore.Release();
            LogManager.ConfigurationReloaded -= LogManagerOnConfigurationReloaded;
        }
    }
}
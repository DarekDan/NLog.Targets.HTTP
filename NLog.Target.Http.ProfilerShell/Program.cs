using System;
using System.Threading.Tasks;
using NLog.Common;

namespace NLog.Target.Http.ProfilerShell
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            ILogger _logger = LogManager.GetCurrentClassLogger();
            var _guid = Guid.NewGuid();
            InternalLogger.Info("Starting");
            Parallel.For(0, 1000000, i => _logger.Info($"{_guid} {i} at {DateTime.Now}"));
            InternalLogger.Info($"Finished {_guid}");
            LogManager.Flush(TimeSpan.FromHours(1));
            LogManager.Shutdown();
        }
    }
}
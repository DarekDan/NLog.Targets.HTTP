using System;
using System.Threading.Tasks;
using NLog.Common;

namespace NLog.Target.Http.ProfilerShell
{
    internal class Program
    {
        private static void Main()
        {
            ILogger logger = LogManager.GetCurrentClassLogger();
            var guid = Guid.NewGuid();
            InternalLogger.Info("Starting");
            Parallel.For(0, 1000000, i => logger.Info($"{guid} {i} at {DateTime.Now}"));
            InternalLogger.Info($"Finished {guid}");
            LogManager.Flush(TimeSpan.FromHours(1));
            LogManager.Shutdown();
        }
    }
}
using System;
using System.Linq;
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
            Parallel.ForEach(Enumerable.Range(0, 1000), i =>
            {
                Parallel.For(0, 1000, i => logger.Info($"{guid} {i} at {DateTime.Now}"));
            });
            LogManager.Flush(TimeSpan.FromHours(1));
            InternalLogger.Info($"Finished {guid}");
            LogManager.Shutdown();
        }
    }
}
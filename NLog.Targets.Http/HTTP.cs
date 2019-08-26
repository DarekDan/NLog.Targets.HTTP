using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog.Common;
using NLog.Config;

namespace NLog.Targets.Http
{
    [Target("HTTP")]
    public class HTTP : TargetWithLayout
    {
        private readonly ConcurrentQueue<string> _taskQueue = new ConcurrentQueue<string>();
        private readonly CancellationTokenSource _terminateProcessor = new CancellationTokenSource();
        
        [RequiredParameter] public string URL { get; set; }

        public string Authorization { get; set; }

        public bool IgnoreSslErrors { get; set; } = true;

        protected override void InitializeTarget()
        {
            Task.Factory.StartNew(() =>
                {
                    while (!_terminateProcessor.IsCancellationRequested)
                    {
                        while (!_taskQueue.IsEmpty)
                        {
                            string message = null;
                            _taskQueue.TryDequeue(out message);
                            if (message != null) SendFast(message);
                        }
                        Thread.Sleep(1);
                    }
                }, _terminateProcessor.Token, TaskCreationOptions.AttachedToParent | TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            base.InitializeTarget();
        }

        protected override void CloseTarget()
        {
            _terminateProcessor.Cancel(false);
            base.CloseTarget();
        }

        protected override void FlushAsync(AsyncContinuation asyncContinuation)
        {
            while (!_taskQueue.IsEmpty) Thread.Sleep(1);
            base.FlushAsync(asyncContinuation);
        }

        protected override void Write(LogEventInfo logEvent)
        {
            _taskQueue.Enqueue(Layout.Render(logEvent));
        }

        private void SendFast(string message)
        {
            var http = (HttpWebRequest) WebRequest.Create(URL);
            http.KeepAlive = false;
            http.Method = "POST";
            if (IgnoreSslErrors)
                http.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
            if (!string.IsNullOrWhiteSpace(Authorization)) http.Headers.Add("Authorization", Authorization);

            var bytes = Encoding.ASCII.GetBytes(message);
            http.ContentLength = bytes.Length;
            using (var os = http.GetRequestStream())
            {
                os.Write(bytes, 0, bytes.Length); //Push it out there
                os.Close();
            }

            using (var response = http.GetResponseAsync())
            {
                using (var stream = response.Result.GetResponseStream())
                {
                    using (var sr = new StreamReader(stream))
                    {
                        var content = sr.ReadToEnd();
                    }
                }
            }
        }
    }
}
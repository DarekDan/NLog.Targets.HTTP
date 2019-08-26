using System;
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
        readonly ConcurrentQueue<Action> _taskQueue = new ConcurrentQueue<Action>();
        readonly CancellationTokenSource _terminateProcessor = new CancellationTokenSource();
        HttpWebRequest http;

        [RequiredParameter] public string URL { get; set; }

        public string Authorization { get; set; }

        public bool IgnoreSslErrors { get; set; } = true;


        protected override void InitializeTarget()
        { 
            http = (HttpWebRequest) WebRequest.Create(URL);
            http.KeepAlive = false;
            http.Method = "POST";
            if (IgnoreSslErrors)
                http.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
            if (!string.IsNullOrWhiteSpace(Authorization)) http.Headers.Add("Authorization", Authorization);

            Task.Factory.StartNew(() =>
                {
                    while (!_terminateProcessor.IsCancellationRequested)
                    {
                        while (!_taskQueue.IsEmpty)
                        {
                            Action action = null;
                            _taskQueue.TryDequeue(out action);
                            action?.Invoke();
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
            while (!_taskQueue.IsEmpty)
            {
                Thread.Sleep(1);
            }
            base.FlushAsync(asyncContinuation);
        }

        protected override void Write(LogEventInfo logEvent)
        {
            _taskQueue.Enqueue(() =>
            {
                var logMessage = Layout.Render(logEvent);
                SendFast(URL, logMessage);
            });
        }

        private void SendFast(string url, string message)
        {
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
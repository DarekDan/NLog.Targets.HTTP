using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
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
        private readonly ConcurrentQueue<StrongBox<string>> _taskQueue = new ConcurrentQueue<StrongBox<string>>();
        private readonly CancellationTokenSource _terminateProcessor = new CancellationTokenSource();
        private readonly SemaphoreSlim _conversationActiveFlag = new SemaphoreSlim(1, 1);

        public HTTP()
        {
            if (BatchSize == 0) ++BatchSize;
            //TODO make it into a parameter
            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.Expect100Continue = false;

            var task = Task.Factory.StartNew(() =>
                {
                    while (!_terminateProcessor.IsCancellationRequested)
                    {
                        var counter = 0;
                        var sb = new StringBuilder();
                        var stack = new List<StrongBox<string>>();
                        while (!_taskQueue.IsEmpty)
                        {
                        
                            if (_taskQueue.TryDequeue(out var message))
                            {
                                ++counter;
                                sb.AppendLine(message.Value);
                                stack.Add(message);
                                if (!_taskQueue.IsEmpty)
                                    sb.AppendLine();
                                message = null;
                            }

                            if (counter == BatchSize)
                            {
                                ProcessChunk(sb, stack);
                                sb.Clear();
                                stack.Clear();
                                counter = 0;
                            }
                        }

                        if (sb.Length > 0)
                            ProcessChunk(sb, stack);
                        Thread.Sleep(1);
                    }
                }, _terminateProcessor.Token, TaskCreationOptions.None,
                TaskScheduler.Default);
            while (task.Status != TaskStatus.Running) Thread.Sleep(1);
        }

        private void ProcessChunk(StringBuilder sb, List<StrongBox<string>> stack)
        {
            if (!SendFast(sb.ToString()))
                stack.ForEach(s => _taskQueue.Enqueue(s));
        }

        [RequiredParameter] public string URL { get; set; }

        public string Authorization { get; set; }

        public bool IgnoreSslErrors { get; set; } = true;

        public bool FlushBeforeShutdown { get; set; } = true;

        public int BatchSize { get; set; }

        protected override void CloseTarget()
        {
            if (FlushBeforeShutdown)
                ProcessCurrentMessages();
            _terminateProcessor.Cancel(false);
            base.CloseTarget();
        }

        protected override void FlushAsync(AsyncContinuation asyncContinuation)
        {
            ProcessCurrentMessages();
            base.FlushAsync(asyncContinuation);
        }

        private void ProcessCurrentMessages()
        {
            // If there are messages to be processed
            // or no flags available 
            // just wait
            while (!_taskQueue.IsEmpty || _conversationActiveFlag.CurrentCount == 0) Thread.Sleep(1);
        }

        protected override void Write(LogEventInfo logEvent)
        {
            var message = Layout.Render(logEvent);
            _taskQueue.Enqueue(new StrongBox<string> { Value = Layout.Render(logEvent) });
        }

        /// <summary>
        /// Sends all the messages
        /// </summary>
        /// <param name="message"></param>
        /// <returns><value>true</value> if succeeded</returns>
        private bool SendFast(string message)
        {
            _conversationActiveFlag.Wait(_terminateProcessor.Token);
            try
            {
                var http = (HttpWebRequest) WebRequest.Create(URL);
                http.KeepAlive = false;
                http.Method = "POST";

                //TODO Make it a Configuration attribute
                http.ContentType = "application/json";
                http.Accept = "application/json";
                http.Timeout = 30;
                http.Proxy = null;

                if (IgnoreSslErrors)
                    http.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
                if (!string.IsNullOrWhiteSpace(Authorization)) http.Headers.Add("Authorization", Authorization);

                var bytes = Encoding.ASCII.GetBytes(message);
                http.ContentLength = bytes.Length;
                using (var os = http.GetRequestStream())
                {
                    os.Write(bytes, 0, bytes.Length); //Push it out there
                    //os.Close();
                }

                using (var response = http.GetResponseAsync())
                {
                    using (var sr = new StreamReader(response.Result.GetResponseStream()))
                    {
                        var content = sr.ReadToEnd();
                    }
                    return !response.IsFaulted;
                }
            } catch(WebException wex)
            {
                //TODO Analyze status
                return false;
            }
            catch (Exception ex)
            {
                //TODO Send to intternal logger
                return false;
            }
            finally
            {
                _conversationActiveFlag.Release();
            }
        }
    }
}
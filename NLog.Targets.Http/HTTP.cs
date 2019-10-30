using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog.Common;
using NLog.Config;

namespace NLog.Targets.Http
{
    [Target("HTTP")]
    // ReSharper disable once InconsistentNaming
    public class HTTP : TargetWithLayout
    {
        private static readonly WebProxy NoProxy = new WebProxy();
        private readonly HttpClient httpClient;
        private readonly WebRequestHandler  handler = new WebRequestHandler();
        private readonly SemaphoreSlim _conversationActiveFlag = new SemaphoreSlim(1, 1);
        private readonly ConcurrentQueue<StrongBox<byte[]>> _taskQueue = new ConcurrentQueue<StrongBox<byte[]>>();
        private readonly CancellationTokenSource _terminateProcessor = new CancellationTokenSource();

        public HTTP()
        {
            httpClient = new HttpClient(handler);
            var task = Task.Factory.StartNew(() =>
                {
                    while (!_terminateProcessor.IsCancellationRequested)
                    {
                        var counter = 0;
                        var sb = new StringBuilder();
                        var stack = new List<StrongBox<byte[]>>();
                        while (!_taskQueue.IsEmpty)
                        {
                            if (_taskQueue.TryDequeue(out var message))
                            {
                                ++counter;
                                sb.AppendLine(InMemoryCompression
                                    ? Utility.Unzip(message.Value)
                                    : Encoding.UTF8.GetString(message.Value));
                                stack.Add(message);
                                if (!_taskQueue.IsEmpty)
                                    sb.AppendLine();
                                // ReSharper disable once RedundantAssignment
                                message = null; //needed to reduce stress on memory 
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

        /// <summary>
        ///     URL to Post to
        /// </summary>
        [RequiredParameter]
        public string Url { get; set; }

        public string Method { get; set; } = "POST";

        public string Authorization { get; set; }

        public bool IgnoreSslErrors { get; set; } = true;

        public bool FlushBeforeShutdown { get; set; } = true;

        private int _batchSize = 1;

        public int BatchSize
        {
            get => _batchSize;
            set => _batchSize = (value < 1) ? 1 : value;
        }

        private int _maxQueueSize = int.MaxValue;

        public int MaxQueueSize
        {
            get => _maxQueueSize;
            set => _maxQueueSize = (value < 1) ? int.MaxValue : value;
        }

        public string ContentType { get; set; } = "application/json";

        public string Accept { get; set; } = "application/json";

        public int DefaultConnectionLimit { get; set; } = ServicePointManager.DefaultConnectionLimit;

        public bool Expect100Continue { get; set; } = ServicePointManager.Expect100Continue;

        public int ConnectTimeout { get; set; } = 30000;

        public bool InMemoryCompression { get; set; } = true;

        public string ProxyUrl { get; set; } = String.Empty;

        public string ProxyUser { get; set; } = String.Empty;

        public string ProxyPassword { get; set; } = String.Empty;

        public bool UseNagleAlgorithm { get; set; } = true;

        private void ProcessChunk(StringBuilder sb, List<StrongBox<byte[]>> stack)
        {
            if (!SendFast(sb.ToString()))
                stack.ForEach(s => _taskQueue.Enqueue(s));
        }

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
            while (_taskQueue.Count > MaxQueueSize) ProcessCurrentMessages();
            _taskQueue.Enqueue(new StrongBox<byte[]>
            {
                Value = InMemoryCompression
                    ? Utility.Zip(Layout.Render(logEvent))
                    : Encoding.UTF8.GetBytes(Layout.Render(logEvent))
            });
        }

        /// <summary>
        ///     Sends all the messages
        /// </summary>
        /// <param name="message"></param>
        /// <returns>
        ///     <value>true</value>
        ///     if succeeded
        /// </returns>
        private bool SendFast(string message)
        {
            _conversationActiveFlag.Wait(_terminateProcessor.Token);
            try
            {
                httpClient.BaseAddress = new Uri(Url);
                httpClient.DefaultRequestHeaders.ExpectContinue = Expect100Continue;
                httpClient.DefaultRequestHeaders.Accept.Clear();
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(Accept));
                httpClient.Timeout = TimeSpan.FromMilliseconds(ConnectTimeout);
                httpClient.DefaultRequestHeaders.ConnectionClose = true;

                handler.UseProxy = !String.IsNullOrWhiteSpace(ProxyUrl);
                handler.Proxy = String.IsNullOrWhiteSpace(ProxyUrl)
                    ? NoProxy
                    : new WebProxy(new Uri(ProxyUrl)) {UseDefaultCredentials = String.IsNullOrWhiteSpace(ProxyUser)};
                if (!String.IsNullOrWhiteSpace(ProxyUser))
                {
                    var cred = ProxyUser.Split('\\');
                    handler.Proxy.Credentials = cred.Length == 1
                        ? new NetworkCredential {UserName = ProxyUser, Password = ProxyPassword}
                        : new NetworkCredential {Domain = cred[0], UserName = cred[1], Password = ProxyPassword};
                }

                if (IgnoreSslErrors)
                    handler.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
                if (!string.IsNullOrWhiteSpace(Authorization))
                {
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(Authorization);
                }

                //TODO
                HttpMethod method = HttpMethod.Post;
                HttpRequestMessage request = new HttpRequestMessage(method, string.Empty);
                request.Content = new StringContent(message, Encoding.UTF8, ContentType);

                return httpClient.SendAsync(request).ContinueWith(responseTask =>
                {
                    HttpResponseMessage httpResponseMessage = responseTask.Result;
                    return httpResponseMessage.IsSuccessStatusCode;
                }).Result;
            }
            catch (WebException wex)
            {
                InternalLogger.Warn(wex, "Failed to communicate over HTTP");
                return false;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn(ex, "Unknown exception occured");
                return false;
            }
            finally
            {
                _conversationActiveFlag.Release();
            }
        }
    }
}
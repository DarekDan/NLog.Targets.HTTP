using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
#if (NETSTANDARD || NET5_0 || NETCOREAPP3_1)
using System.Net.Security;
#endif

namespace NLog.Targets.Http
{
    [Target("HTTP")]
    // ReSharper disable once InconsistentNaming
    public class HTTP : TargetWithLayout
    {
        private static readonly Dictionary<string, HttpMethod> AvailableHttpMethods = new Dictionary<string, HttpMethod>
            { { "post", HttpMethod.Post }, { "get", HttpMethod.Get } };

        private readonly SemaphoreSlim _conversationActiveFlag = new SemaphoreSlim(1, 1);
        private readonly ConcurrentStack<string> _propertiesChanged = new ConcurrentStack<string>();
        private readonly ConcurrentQueue<StrongBox<byte[]>> _taskQueue = new ConcurrentQueue<StrongBox<byte[]>>();
        private readonly CancellationTokenSource _terminateProcessor = new CancellationTokenSource();
        private string _accept = "application/json";
        private Layout _authorization;

        private int _batchSize = 1;
        private int _connectTimeout = 30000;
        private bool _expect100Continue = ServicePointManager.Expect100Continue;
#if NET5_0 || NETCOREAPP3_1
        private SocketsHttpHandler _handler;
#elif NETSTANDARD
        private HttpClientHandler _handler;
#else
        private WebRequestHandler _handler;
#endif
        private HttpClient _httpClient;
        private bool _ignoreSslErrors = true;
        private bool _hasHttpError;

        private int _maxQueueSize = int.MaxValue;
        private Layout _proxyPassword = string.Empty;
        private Layout _proxyUrl = Layout.FromString(string.Empty);
        private Layout _proxyUser = string.Empty;
        private Layout _url = Layout.FromString(string.Empty);

        /// <summary>
        ///     Invoked when the application is unable to flush due to a HTTP related error.
        /// </summary>
        public static event EventHandler<FlushErrorEventArgs> FlushError = (sender, args) =>
        {
            InternalLogger.Error(args.FailedMessage);
        };

        /// <summary>
        ///     URL to Post to
        /// </summary>
        [RequiredParameter]
        public Layout Url
        {
            get => _url;
            set
            {
                if (value == _url) return;
                _url = value;
                NotifyPropertyChanged(nameof(Url));
            }
        }

        public string Method { get; set; } = "POST";

        public Layout Authorization
        {
            get => _authorization;
            set
            {
                if (value == _authorization) return;
                _authorization = value;
                NotifyPropertyChanged(nameof(Authorization));
            }
        }

        public bool IgnoreSslErrors
        {
            get => _ignoreSslErrors;
            set
            {
                if (value == _ignoreSslErrors) return;
                _ignoreSslErrors = value;
                NotifyPropertyChanged(nameof(IgnoreSslErrors));
            }
        }

        public bool FlushBeforeShutdown { get; set; } = true;

        /// <summary>
        ///     The timeout between attempted HTTP requests.
        /// </summary>
        public int HttpErrorRetryTimeout { get; set; } = 500;

        public bool KeepAlive { get; set; }

        public int BatchSize
        {
            get => _batchSize;
            set => _batchSize = value < 1 ? 1 : value;
        }

        public bool BatchAsJsonArray { get; set; } = false;

        public int MaxQueueSize
        {
            get => _maxQueueSize;
            set => _maxQueueSize = value < 1 ? int.MaxValue : value;
        }

        public string ContentType { get; set; } = "application/json";

        public string Accept
        {
            get => _accept;
            set
            {
                if (value == _accept) return;
                _accept = value;
                NotifyPropertyChanged(nameof(Accept));
            }
        }

        [ArrayParameter(typeof(NHttpHeader), "header")]
        public IList<NHttpHeader> Headers { get; set; } = new List<NHttpHeader>();

        // ReSharper disable once UnusedMember.Global
        [Obsolete] public int DefaultConnectionLimit { get; set; } = ServicePointManager.DefaultConnectionLimit;

        public bool Expect100Continue
        {
            get => _expect100Continue;
            set
            {
                if (value == _expect100Continue) return;
                _expect100Continue = value;
                NotifyPropertyChanged(nameof(Expect100Continue));
            }
        }

        public int ConnectTimeout
        {
            get => _connectTimeout;
            set
            {
                if (value == _connectTimeout) return;
                _connectTimeout = value;
                NotifyPropertyChanged(nameof(ConnectTimeout));
            }
        }

        public bool InMemoryCompression { get; set; } = false;

        public Layout ProxyUrl
        {
            get => _proxyUrl;
            set
            {
                if (value == _proxyUrl) return;
                _proxyUrl = value;
                NotifyPropertyChanged(nameof(ProxyUrl));
            }
        }

        public Layout ProxyUser
        {
            get => _proxyUser;
            set
            {
                if (value == _proxyUser) return;
                _proxyUser = value;
                NotifyPropertyChanged(nameof(ProxyUser));
            }
        }

        public Layout ProxyPassword
        {
            get => _proxyPassword;
            set
            {
                if (value == _proxyPassword) return;
                _proxyPassword = value;
                NotifyPropertyChanged(nameof(ProxyPassword));
            }
        }

        // ReSharper disable once UnusedMember.Global
        // ReSharper disable once IdentifierTypo
        [Obsolete] public bool UseNagleAlgorithm { get; set; } = true;

        private async Task ProcessChunk(StringBuilder sb, List<StrongBox<byte[]>> stack)
        {
            if (!await SendFast(sb.ToString()).ConfigureAwait(false))
                stack.ForEach(s => _taskQueue.Enqueue(s));
        }

        protected override void InitializeTarget()
        {
            base.InitializeTarget();
            var token = _terminateProcessor.Token;
            _ = Task.Run(() => Start(token), token);
        }

        private async Task Start(CancellationToken cancellationToken)
        {
            var stack = new List<StrongBox<byte[]>>();
            while (!cancellationToken.IsCancellationRequested)
            {
                stack.Clear();
                var builder = BuildChunk(stack, cancellationToken);

                if (builder.Length > 0)
                {
                    if (_hasHttpError)
                    {
                        try
                        {
                            await _conversationActiveFlag.WaitAsync(_terminateProcessor.Token);
                            var delay = Task.Delay(1, CancellationToken.None);
                            FlushError?.Invoke(this, new FlushErrorEventArgs(builder.ToString()));
                            await delay; // ensure semaphore is entered for at least 1ms for flush detection.
                        }
                        finally
                        {
                            _conversationActiveFlag.Release();
                        }
                    }
                    else
                    {
                        await ProcessChunk(builder, stack).ConfigureAwait(false);

                        if (_hasHttpError)
                            try
                            {
                                // Reduce stress
                                await Task.Delay(HttpErrorRetryTimeout, cancellationToken).ConfigureAwait(false);
                            }
                            catch (TaskCanceledException tce)
                            {
                                InternalLogger.Info($"HTTP Logger {tce.GetBaseException()}");
                            }
                    }
                }

                await Task.Delay(1, cancellationToken);
            }
        }

        private StringBuilder BuildChunk(List<StrongBox<byte[]>> stack, CancellationToken flushToken)
        {
            var builder = new StringBuilder();
            var counter = 0;
            if (BatchAsJsonArray)
                builder.Append("[");
            while (!_taskQueue.IsEmpty)
            {
                if (_taskQueue.TryDequeue(out var message))
                {
                    ++counter;
                    builder.AppendLine(InMemoryCompression
                        ? Utility.Unzip(message.Value)
                        : Encoding.UTF8.GetString(message.Value));
                    stack.Add(message);
                    // ReSharper disable once RedundantAssignment
                    message = null; //needed to reduce stress on memory 
                }

                if (counter == BatchSize && !flushToken.IsCancellationRequested) break;
                if (!_taskQueue.IsEmpty)
                    builder.Append(BatchAsJsonArray ? ", " : Environment.NewLine);
            }

            if (BatchAsJsonArray)
                builder.Append("]");
            return builder;
        }

        protected override void CloseTarget()
        {
            if (FlushBeforeShutdown)
                AwaitCurrentMessagesToProcess();
            _terminateProcessor.Cancel(false);
            base.CloseTarget();
        }

        protected override void FlushAsync(AsyncContinuation asyncContinuation)
        {
            InternalLogger.Info($"Flushing {_taskQueue.Count} events");
            AwaitCurrentMessagesToProcess();
            base.FlushAsync(asyncContinuation);
        }

        private void AwaitCurrentMessagesToProcess()
        {
            // If there are messages to be processed
            // or no flags available 
            // just wait
            while (!_taskQueue.IsEmpty || _conversationActiveFlag.CurrentCount == 0) Thread.Sleep(1);
        }

        protected override void Write(LogEventInfo logEvent)
        {
            SafeEnqueue(logEvent);
        }

        private void SafeEnqueue(LogEventInfo logEvent)
        {
            while (_taskQueue.Count >= MaxQueueSize) AwaitCurrentMessagesToProcess();
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
        private async Task<bool> SendFast(string message)
        {
            await _conversationActiveFlag.WaitAsync(_terminateProcessor.Token).ConfigureAwait(false);
            try
            {
                ResetHttpClientIfNeeded();
                var method = GetHttpMethodsToUseOrDefault();
                var request = new HttpRequestMessage(method, string.Empty)
                {
                    Content = new StringContent(message, Encoding.UTF8, ContentType)
                };


                var httpResponseMessage = await _httpClient.SendAsync(request).ConfigureAwait(false);
#if NETFRAMEWORK || NETSTANDARD
                if ((int)httpResponseMessage.StatusCode == 429)
#else
                if (httpResponseMessage.StatusCode == HttpStatusCode.TooManyRequests)
#endif
                    // We should respect 429.
                    await Task.Delay(7500).ConfigureAwait(false);

                var isSuccess = httpResponseMessage.IsSuccessStatusCode;
                _hasHttpError = !isSuccess;
                return isSuccess;
            }
            catch (WebException)
            {
                _hasHttpError = true;
                return false;
            }
            catch (HttpRequestException)
            {
                _hasHttpError = true;
                return false;
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _hasHttpError = true;
                return false;
            }
            catch (TaskCanceledException ex)
            {
                if (ex.Message.Contains("Timeout"))
                {
                    _hasHttpError = true;
                    return false;
                } else {
                    InternalLogger.Warn(ex, "Unknown exception occured");
                    return false;
                }
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

        private HttpMethod GetHttpMethodsToUseOrDefault()
        {
            return AvailableHttpMethods[Method.ToLower()] ?? HttpMethod.Post;
        }

        private AuthenticationHeaderValue GetAuthorizationHeader()
        {
            var parts = Authorization.Render(LogEventInfo.CreateNullEvent()).Split(' ');
            return parts.Length == 1
                ? new AuthenticationHeaderValue(parts[0])
                : new AuthenticationHeaderValue(parts[0], string.Join(" ", parts.Skip(1)));
        }

        private void NotifyPropertyChanged(string name)
        {
            _propertiesChanged.Push(name);
        }

        private void ResetHttpClientIfNeeded()
        {
            if (!_propertiesChanged.Any()) return;
            lock (_propertiesChanged)
            {
                // ReSharper disable once UseObjectOrCollectionInitializer
#if NET5_0 || NETCOREAPP3_1
                    _handler = new SocketsHttpHandler();
#elif NETSTANDARD
                    _handler = new HttpClientHandler();
#else
                _handler = new WebRequestHandler();
#endif
                var nullEvent = LogEventInfo.CreateNullEvent();
                var proxyUrl = ProxyUrl?.Render(nullEvent);
                _handler.UseProxy = !string.IsNullOrWhiteSpace(proxyUrl);
                _httpClient = new HttpClient(_handler)
                {
                    BaseAddress = new Uri(Url.Render(nullEvent)),
                    Timeout = TimeSpan.FromMilliseconds(ConnectTimeout)
                };

                if (!KeepAlive)
                {
                    _httpClient.DefaultRequestHeaders.ConnectionClose = true;
                    _httpClient.DefaultRequestHeaders.ExpectContinue = Expect100Continue;
                }
                else
                {
                    _httpClient.DefaultRequestHeaders.Add("Keep-Alive", "timeout=5, max=1000");
                }

                _httpClient.DefaultRequestHeaders.Accept.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(Accept));

                foreach (var header in Headers.Where(w =>
                    !string.IsNullOrWhiteSpace(w.Name) && !string.IsNullOrWhiteSpace(w.Value.Render(nullEvent))))
                    _httpClient.DefaultRequestHeaders.Add(header.Name, header.Value.Render(nullEvent));

                if (_handler.UseProxy)
                {
                    var proxyUser = ProxyUser.Render(nullEvent);
                    var useDefaultCredentials = string.IsNullOrWhiteSpace(proxyUser);

                    // UseProxy will not be set, if proxyUrl is null or whitespace (above, few lines)
                    // ReSharper disable once AssignNullToNotNullAttribute
                    _handler.Proxy = new WebProxy(new Uri(proxyUrl))
                        { UseDefaultCredentials = useDefaultCredentials };
                    if (!useDefaultCredentials)
                    {
                        var cred = proxyUser.Split('\\');
                        _handler.Proxy.Credentials = cred.Length == 1
                            ? new NetworkCredential
                                { UserName = proxyUser, Password = ProxyPassword.Render(nullEvent) }
                            : new NetworkCredential
                                { Domain = cred[0], UserName = cred[1], Password = ProxyPassword.Render(nullEvent) };
                    }
                }

                if (!string.IsNullOrWhiteSpace(Authorization.Render(nullEvent)))
                    _httpClient.DefaultRequestHeaders.Authorization = GetAuthorizationHeader();

                if (IgnoreSslErrors)
                {
#if NETCOREAPP3_0 || NET5_0 || NETCOREAPP3_1
                        _handler.SslOptions = new SslClientAuthenticationOptions{RemoteCertificateValidationCallback =
 (sender, certificate, chain, errors) => true};
#elif NETSTANDARD
                        _handler.ServerCertificateCustomValidationCallback = (message,certificate,chain,errors)=>true;
#else
                    _handler.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
#endif
                }

                _propertiesChanged.Clear();
            }
        }
    }
}
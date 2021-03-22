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
#if (NETCORE30 || NETSTANDARD21 || NET5_0 || NETCOREAPP3_1)
using System.Net.Security;
#endif

namespace NLog.Targets.Http
{
    [Target("HTTP")]
    // ReSharper disable once InconsistentNaming
    public class HTTP : TargetWithLayout
    {
        private static readonly Dictionary<string, HttpMethod> AvailableHttpMethods = new Dictionary<string, HttpMethod>
            {{"post", HttpMethod.Post}, {"get", HttpMethod.Get}};

        private readonly SemaphoreSlim _conversationActiveFlag = new SemaphoreSlim(1, 1);
        private readonly ConcurrentStack<string> _propertiesChanged = new ConcurrentStack<string>();
        private readonly ConcurrentQueue<StrongBox<byte[]>> _taskQueue = new ConcurrentQueue<StrongBox<byte[]>>();
        private readonly CancellationTokenSource _terminateProcessor = new CancellationTokenSource();
        private readonly StringBuilder builder = new StringBuilder();
        private CancellationTokenSource _flushTokenSource = new CancellationTokenSource();
        private string _accept = "application/json";
        private string _authorization;

        private int _batchSize = 1;
        private int _connectTimeout = 30000;
        private bool _expect100Continue = ServicePointManager.Expect100Continue;
#if (NETCORE30 || NET5_0 || NETCOREAPP3_1)
        private SocketsHttpHandler _handler;
#elif NETSTANDARD21
        private HttpClientHandler _handler;
#else
        private WebRequestHandler _handler;
#endif
        private HttpClient _httpClient;
        private bool _ignoreSslErrors = true;
        private bool hasHttpError;

        private int _maxQueueSize = int.MaxValue;
        private string _proxyPassword = string.Empty;
        private string _proxyUrl = string.Empty;
        private string _proxyUser = string.Empty;
        private Layout _url;

        /// <summary>
        /// Invoked when the application is unable to flush due to a HTTP related error.
        /// </summary>
        public static event EventHandler<FlushErrorEventArgs> FlushError;

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

        public string Authorization
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
        /// The timeout between attempted HTTP requests.
        /// </summary>
        public int HttpErrorRetryTimeout { get; set; } = 500;

        public bool Keepalive { get; set; }

        public int BatchSize
        {
            get => _batchSize;
            set => _batchSize = value < 1 ? 1 : value;
        }

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

        public bool InMemoryCompression { get; set; } = true;

        public string ProxyUrl
        {
            get => _proxyUrl;
            set
            {
                if (value == _proxyUrl) return;
                _proxyUrl = value;
                NotifyPropertyChanged(nameof(ProxyUrl));
            }
        }

        public string ProxyUser
        {
            get => _proxyUser;
            set
            {
                if (value == _proxyUser) return;
                _proxyUser = value;
                NotifyPropertyChanged(nameof(ProxyUser));
            }
        }

        public string ProxyPassword
        {
            get => _proxyPassword;
            set
            {
                if (value == _proxyPassword) return;
                _proxyPassword = value;
                NotifyPropertyChanged(nameof(ProxyPassword));
            }
        }

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
                builder.Clear();
                stack.Clear();
                var flushToken = _flushTokenSource.Token;
                BuildChunk(stack, flushToken);

                if (builder.Length > 0)
                {
                    if (flushToken.IsCancellationRequested && hasHttpError)
                    {
                        try
                        {
                            _conversationActiveFlag.Wait(_terminateProcessor.Token);
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

                        if (hasHttpError)
                        {
                            try
                            {
                                // Reduce stress
                                await Task.Delay(HttpErrorRetryTimeout, flushToken).ConfigureAwait(false);
                            }
                            catch (TaskCanceledException) { }
                        }
                    }
                }

                await Task.Delay(1, cancellationToken);
            }
        }

        private void BuildChunk(List<StrongBox<byte[]>> stack, CancellationToken flushToken)
        {
            int counter = 0;
            while (!_taskQueue.IsEmpty)
            {
                if (_taskQueue.TryDequeue(out var message))
                {
                    ++counter;
                    builder.AppendLine(InMemoryCompression
                        ? Utility.Unzip(message.Value)
                        : Encoding.UTF8.GetString(message.Value));
                    stack.Add(message);
                    if (!_taskQueue.IsEmpty)
                        builder.AppendLine();
                    // ReSharper disable once RedundantAssignment
                    message = null; //needed to reduce stress on memory 
                }

                if (counter == BatchSize && !flushToken.IsCancellationRequested)
                {
                    break;
                }
            }
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
            AwaitCurrentMessagesToProcess();
            base.FlushAsync(asyncContinuation);
        }

        private void AwaitCurrentMessagesToProcess()
        {
            // If there are messages to be processed
            // or no flags available 
            // just wait
            _flushTokenSource.Cancel(false);
            while (!_taskQueue.IsEmpty || _conversationActiveFlag.CurrentCount == 0) Thread.Sleep(1);
            _flushTokenSource.Dispose();
            _flushTokenSource = new CancellationTokenSource();
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
            _conversationActiveFlag.Wait(_terminateProcessor.Token);
            try
            {
                ResetHttpClientIfNeeded();
                var method = GetHttpMethodsToUseOrDefault();
                var request = new HttpRequestMessage(method, string.Empty)
                {
                    Content = new StringContent(message, Encoding.UTF8, ContentType),
                    Keepalive = Keepalive
                };


                var httpResponseMessage = await _httpClient.SendAsync(request).ConfigureAwait(false);
                if (httpResponseMessage.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    // We should respect 429.
                    await Task.Delay(7500).ConfigureAwait(false);
                }

                var isSuccess = httpResponseMessage.IsSuccessStatusCode;
                hasHttpError = !isSuccess;
                return isSuccess;
            }
            catch (WebException)
            {
                hasHttpError = true;
                return false;
            }
            catch (HttpRequestException)
            {
                hasHttpError = true;
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

        private HttpMethod GetHttpMethodsToUseOrDefault()
        {
            return AvailableHttpMethods[Method.ToLower()] ?? HttpMethod.Post;
        }

        private AuthenticationHeaderValue GetAuthorizationHeader()
        {
            var parts = Authorization.Split(' ');
            return parts.Length == 1
                ? new AuthenticationHeaderValue(Authorization)
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
#if (NETCORE30 || NET5_0 || NETCOREAPP3_1)
                    _handler = new SocketsHttpHandler();
#elif NETSTANDARD21
                    _handler = new HttpClientHandler();
#else
                _handler = new WebRequestHandler();
#endif
                _handler.UseProxy = !string.IsNullOrWhiteSpace(ProxyUrl);
                _httpClient = new HttpClient(_handler)
                {
                    BaseAddress = new Uri(Url.Render(LogEventInfo.CreateNullEvent())),
                    Timeout = TimeSpan.FromMilliseconds(ConnectTimeout)
                };

                if (!Keepalive)
                {
                    _httpClient.DefaultRequestHeaders.ConnectionClose = true;
                    _httpClient.DefaultRequestHeaders.ExpectContinue = Expect100Continue;
                }

                _httpClient.DefaultRequestHeaders.Accept.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(Accept));

                if (_handler.UseProxy)
                {
                    var useDefaultCredentials = string.IsNullOrWhiteSpace(ProxyUser);
                    _handler.Proxy = new WebProxy(new Uri(ProxyUrl))
                    { UseDefaultCredentials = useDefaultCredentials };
                    if (!useDefaultCredentials)
                    {
                        var cred = ProxyUser.Split('\\');
                        _handler.Proxy.Credentials = cred.Length == 1
                            ? new NetworkCredential { UserName = ProxyUser, Password = ProxyPassword }
                            : new NetworkCredential
                            { Domain = cred[0], UserName = cred[1], Password = ProxyPassword };
                    }
                }

                if (!string.IsNullOrWhiteSpace(Authorization))
                {
                    _httpClient.DefaultRequestHeaders.Authorization = GetAuthorizationHeader();
                }
                if (IgnoreSslErrors)

#if (NETCOREAPP3_0 || NET5_0 || NETCOREAPP3_1)
                        _handler.SslOptions = new SslClientAuthenticationOptions{RemoteCertificateValidationCallback =
 (sender, certificate, chain, errors) => true};
#elif NETSTANDARD21
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
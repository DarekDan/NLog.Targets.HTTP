using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
#if (NETSTANDARD || NETCOREAPP)
using System.Net.Security;
#endif

namespace NLog.Targets.Http
{
    [Target("HTTP")]
    // ReSharper disable once InconsistentNaming
    public class HTTP : TargetWithLayout
    {
        private static readonly byte[] JsonArrayStart = Encoding.UTF8.GetBytes("[");
        private static readonly byte[] JsonArrayEnd = Encoding.UTF8.GetBytes("]");
        private static readonly byte[] JsonArrayDelimit = Encoding.UTF8.GetBytes(", ");
        private static readonly byte[] JsonNewline = Encoding.UTF8.GetBytes(Environment.NewLine);

        private readonly SemaphoreSlim _conversationActiveFlag = new SemaphoreSlim(1, 1);
        private readonly ConcurrentStack<string> _propertiesChanged = new ConcurrentStack<string>();
        private readonly ConcurrentQueue<byte[]> _taskQueue = new ConcurrentQueue<byte[]>();
        private readonly CancellationTokenSource _terminateProcessor = new CancellationTokenSource();
        private string _accept = "application/json";
        private Layout _authorization;

        private int _batchSize = 1;
        private int _connectTimeout = 30000;
        private bool _expect100Continue = ServicePointManager.Expect100Continue;
        private HttpClient _httpClient;
        private bool _ignoreSslErrors = true;
        private bool _hasHttpError;

        private int _maxQueueSize = int.MaxValue;
        private Layout _proxyPassword = string.Empty;
        private Layout _proxyUrl = Layout.FromString(string.Empty);
        private Layout _proxyUser = string.Empty;
        private Layout _url = Layout.FromString(string.Empty);
        private string _contentType = "application/json";
        private MediaTypeHeaderValue _contentTypeHeader = new MediaTypeHeaderValue("application/json") { CharSet = Encoding.UTF8.WebName };

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

        public string Method
        {
            get => _method.ToString();
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    _method = HttpMethodVerb.POST;
                else if (Enum.TryParse<HttpMethodVerb>(value.Trim().ToUpper(), out var method))
                    _method = method;
                else
                    _method = HttpMethodVerb.POST;
            }
        }
        private HttpMethodVerb _method = HttpMethodVerb.POST;

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

        public string ContentType
        {
            get => _contentType;
            set
            {
                _contentType = string.IsNullOrWhiteSpace(value) ? "application/json" : value;
                _contentTypeHeader = new MediaTypeHeaderValue(_contentType) { CharSet = Encoding.UTF8.WebName };
            }
        }

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

        public HTTP()
        {
            OptimizeBufferReuse = true; // Optimize RenderLogEvent()
        }

        private async Task ProcessChunk(ArraySegment<byte> bytes, List<byte[]> stack)
        {
            if (!await SendFast(bytes).ConfigureAwait(false))
            {
                foreach (var item in stack)
                    _taskQueue.Enqueue(item);
            }
        }

        protected override void InitializeTarget()
        {
            base.InitializeTarget();
            var token = _terminateProcessor.Token;
            _ = Task.Run(() => Start(token), token);
        }

        private async Task Start(CancellationToken cancellationToken)
        {
            var stack = new List<byte[]>();
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_taskQueue.IsEmpty)
                {
                    await Task.Delay(1, CancellationToken.None).ConfigureAwait(false);
                    continue;
                }

                if (_hasHttpError)
                    try
                    {
                        await _conversationActiveFlag.WaitAsync(_terminateProcessor.Token);
                        await Task.Delay(HttpErrorRetryTimeout, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        InternalLogger.Info($"HTTP Logger: {exception.Message}");
                    }
                    finally
                    {
                        _hasHttpError = false;
                        _conversationActiveFlag.Release();
                    }

                stack.Clear();
                var builder = BuildChunk(stack, cancellationToken);
                await ProcessChunk(builder, stack).ConfigureAwait(false);
            }
        }

        private ArraySegment<byte> BuildChunk(List<byte[]> stack, CancellationToken flushToken)
        {
            _taskQueue.TryPeek(out var peek);

            using (var memoryStream = new MemoryStream((int)(BatchSize * (peek?.Length ?? 0) * 1.1)))
            {
                var counter = 0;
                if (BatchAsJsonArray)
                    memoryStream.Append(JsonArrayStart);

                while (_taskQueue.TryDequeue(out var message))
                {
                    if (counter > 0)
                        memoryStream.Append(BatchAsJsonArray ? JsonArrayDelimit : JsonNewline);

                    ++counter;
                    memoryStream.Append(InMemoryCompression ? Utility.UnzipAsBytes(message) : message);
                    stack.Add(message);

                    if (counter == BatchSize && !flushToken.IsCancellationRequested) break;
                }

                if (BatchAsJsonArray)
                    memoryStream.Append(JsonArrayEnd);
                return new ArraySegment<byte>(memoryStream.GetBuffer(), 0, (int)memoryStream.Length);
            }
        }

        protected override void CloseTarget()
        {
            if (FlushBeforeShutdown)
                AwaitCurrentMessagesToProcess().Wait();
            _terminateProcessor.Cancel(false);
            base.CloseTarget();
        }

        protected override void FlushAsync(AsyncContinuation asyncContinuation)
        {
            InternalLogger.Info($"Flushing {_taskQueue.Count} events");
            AwaitCurrentMessagesToProcess().ContinueWith(task => asyncContinuation(task.Exception));
        }

        private async Task AwaitCurrentMessagesToProcess()
        {
            // If there are messages to be processed
            // or no flags available 
            // just wait
            while (!_taskQueue.IsEmpty || _conversationActiveFlag.CurrentCount == 0)
                await Task.Delay(1, CancellationToken.None).ConfigureAwait(false);
        }

        protected override void Write(LogEventInfo logEvent)
        {
            var payload = RenderLogEvent(Layout, logEvent);
            // NLogs Write is synchronous
            SafeEnqueue(payload).Wait();
        }

        private async Task SafeEnqueue(string payload)
        {
            while (_taskQueue.Count >= MaxQueueSize) await AwaitCurrentMessagesToProcess().ConfigureAwait(false);

            _taskQueue.Enqueue(InMemoryCompression
                    ? Utility.Zip(payload)
                    : Encoding.UTF8.GetBytes(payload));
        }

        /// <summary>
        ///     Sends all the messages
        /// </summary>
        /// <param name="message"></param>
        /// <returns>
        ///     <value>true</value>
        ///     if succeeded
        /// </returns>
        private async Task<bool> SendFast(ArraySegment<byte> message)
        {
            await _conversationActiveFlag.WaitAsync(_terminateProcessor.Token).ConfigureAwait(false);
            try
            {
                ResetHttpClientIfNeeded();
                var method = GetHttpMethodsToUseOrDefault();
                var request = new HttpRequestMessage(method, string.Empty)
                {
                    Content = new ByteArrayContent(message.Array, message.Offset, message.Count)
                };
                request.Content.Headers.ContentType = _contentTypeHeader;

                var httpResponseMessage = await _httpClient.SendAsync(request).ConfigureAwait(false);
#if NETFRAMEWORK || NETSTANDARD
                if ((int)httpResponseMessage.StatusCode == 429)
#else
                if (httpResponseMessage.StatusCode == HttpStatusCode.TooManyRequests)
#endif
                    // Respect 429.
                    await Task.Delay(7500).ConfigureAwait(false);

                _hasHttpError = !httpResponseMessage.IsSuccessStatusCode;
                return !_hasHttpError;
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
            catch (TaskCanceledException taskCanceledException) when
                (taskCanceledException.InnerException is TimeoutException)
            {
                _hasHttpError = true;
                return false;
            }
            catch (TaskCanceledException taskCanceledException)
            {
                InternalLogger.Warn(taskCanceledException, "Unknown timeout exception occurred");
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
            switch (_method)
            {
                case HttpMethodVerb.GET: return HttpMethod.Get;
                case HttpMethodVerb.POST:
                default:
                    return HttpMethod.Post;
            }
        }

        private static AuthenticationHeaderValue GetAuthorizationHeader(string authorization)
        {
            var parts = authorization.Split(' ');
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
#if NETCOREAPP
                var handler = new SocketsHttpHandler();
#elif NETSTANDARD
                var handler = new HttpClientHandler();
#else
                var handler = new WebRequestHandler();
#endif
                var nullEvent = LogEventInfo.CreateNullEvent();
                var proxyUrl = ProxyUrl?.Render(nullEvent);
                handler.UseProxy = !string.IsNullOrWhiteSpace(proxyUrl);
                _httpClient = new HttpClient(handler)
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
                             w != null &&
                             !string.IsNullOrWhiteSpace(w.Name) &&
                             !string.IsNullOrWhiteSpace(w.Value?.Render(nullEvent))))
                    _httpClient.DefaultRequestHeaders.Add(header.Name, header.Value.Render(nullEvent));

                if (handler.UseProxy)
                {
                    var proxyUser = ProxyUser?.Render(nullEvent);
                    var useDefaultCredentials = string.IsNullOrWhiteSpace(proxyUser);

                    // UseProxy will not be set, if proxyUrl is null or whitespace (above, few lines)
                    // ReSharper disable once AssignNullToNotNullAttribute
                    handler.Proxy = new WebProxy(new Uri(proxyUrl))
                        { UseDefaultCredentials = useDefaultCredentials };
                    if (!useDefaultCredentials)
                    {
                        var cred = proxyUser.Split('\\');
                        handler.Proxy.Credentials = cred.Length == 1
                            ? new NetworkCredential
                                { UserName = proxyUser, Password = ProxyPassword?.Render(nullEvent) ?? string.Empty }
                            : new NetworkCredential
                            {
                                Domain = cred[0], UserName = cred[1],
                                Password = ProxyPassword?.Render(nullEvent) ?? string.Empty
                            };
                    }
                }

                var authorization = Authorization?.Render(nullEvent);
                if (!string.IsNullOrWhiteSpace(authorization))
                    _httpClient.DefaultRequestHeaders.Authorization = GetAuthorizationHeader(authorization);

                if (IgnoreSslErrors)
                {
#if NETCOREAPP
                    handler.SslOptions = new SslClientAuthenticationOptions{
                        RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
                    };
#elif NETSTANDARD
                    handler.ServerCertificateCustomValidationCallback = (message,certificate,chain,errors) => true;
#else
                    handler.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
#endif
                }

                _propertiesChanged.Clear();
            }
        }
    }
}
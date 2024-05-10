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
using NLog.Config;
using NLog.Layouts;
#if (NETSTANDARD || NETCOREAPP)
using System.Net.Security;
#endif

namespace NLog.Targets.Http
{
    [Target("HTTP")]
    // ReSharper disable once InconsistentNaming
    public class HTTP : AsyncTaskTarget
    {
        private static readonly Encoding _utf8Encoding = new UTF8Encoding(false);   // No PreAmble BOM
        private static readonly byte[] JsonArrayStart = _utf8Encoding.GetBytes("[");
        private static readonly byte[] JsonArrayEnd = _utf8Encoding.GetBytes("]");

        private readonly ConcurrentStack<string> _propertiesChanged = new ConcurrentStack<string>();
        private string _accept = "application/json";
        private Layout _authorization;

        private int _connectTimeout = 30000;
        private bool _expect100Continue = ServicePointManager.Expect100Continue;
        private HttpClient _httpClient;
        private bool _ignoreSslErrors = true;

        private Layout _proxyPassword = string.Empty;
        private Layout _proxyUrl = Layout.FromString(string.Empty);
        private Layout _proxyUser = string.Empty;
        private Layout _url = Layout.FromString(string.Empty);
        private string _contentType = "application/json";
        private MediaTypeHeaderValue _contentTypeHeader = new MediaTypeHeaderValue("application/json") { CharSet = _utf8Encoding.WebName };

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

        [Obsolete] public bool FlushBeforeShutdown { get; set; } = true;

        /// <summary>
        ///     The timeout between attempted HTTP requests.
        /// </summary>
        public int HttpErrorRetryTimeout
        {
            get => RetryDelayMilliseconds;
            set => RetryDelayMilliseconds = value;
        }

        public bool KeepAlive { get; set; }

        public bool BatchAsJsonArray { get; set; } = false;

        public int MaxQueueSize
        {
            get => OverflowAction == Wrappers.AsyncTargetWrapperOverflowAction.Grow ? int.MaxValue : QueueLimit;
            set
            {
                if (value < 1 || value == int.MaxValue)
                {
                    OverflowAction = Wrappers.AsyncTargetWrapperOverflowAction.Grow;    // No limit
                    QueueLimit = 10000;
                }
                else
                {
                    OverflowAction = Wrappers.AsyncTargetWrapperOverflowAction.Block;   // Block on limit
                    QueueLimit = value;
                }
            }
        }

        public string ContentType
        {
            get => _contentType;
            set
            {
                if (value == _contentType) return;
                _contentType = string.IsNullOrWhiteSpace(value) ? "application/json" : value;
                _contentTypeHeader = new MediaTypeHeaderValue(_contentType) { CharSet = _utf8Encoding.WebName };
                NotifyPropertyChanged(nameof(ContentType));
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

        [Obsolete] public bool InMemoryCompression { get; set; } = false;

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
            BatchSize = 1; // No batching by default
            OverflowAction = Wrappers.AsyncTargetWrapperOverflowAction.Grow; // No queue-limit by default
            RetryCount = int.MaxValue; // Infinite retry by default
            HttpErrorRetryTimeout = 500;
        }


        /// <inheritdoc />
        protected override void CloseTarget()
        {
            var oldHttpClient = _httpClient;
            NotifyPropertyChanged(nameof(Url)); // Ensure to create fresh HttpClient
            _httpClient = null;
            oldHttpClient?.Dispose();
            base.CloseTarget();
        }

        protected override Task WriteAsyncTask(LogEventInfo logEvent, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();  // Never called
        }

        protected override Task WriteAsyncTask(IList<LogEventInfo> logEvents, CancellationToken cancellationToken)
        {
            var chunk = BuildChunk(logEvents);
            return SendFast(chunk);
        }

        private ArraySegment<byte> BuildChunk(IList<LogEventInfo> logEvents)
        {
            if (logEvents.Count == 0)
                return new ArraySegment<byte>(null, 0, 0);

            var encoding = _utf8Encoding;
            var firstPayloadString = RenderLogEvent(Layout, logEvents[0]);
            var firstPayloadBytes = encoding.GetBytes(firstPayloadString);

            using (var ms = new MemoryStream((int)(logEvents.Count * firstPayloadBytes.Length * 1.1)))
            {
                if (BatchAsJsonArray)
                    ms.Append(JsonArrayStart);

                ms.Append(firstPayloadBytes);

                if (logEvents.Count > 1)
                {
                    using (var sw = new StreamWriter(ms, encoding, bufferSize: 1024, leaveOpen: true))
                    {
                        for (int i = 1; i < logEvents.Count; ++i)
                        {
                            sw.Write(BatchAsJsonArray ? ", " : Environment.NewLine);

                            var payload = RenderLogEvent(Layout, logEvents[i]);
                            sw.Write(payload);
                        }
                    }
                }

                if (BatchAsJsonArray)
                    ms.Append(JsonArrayEnd);

                return new ArraySegment<byte>(ms.GetBuffer(), 0, (int)ms.Length);
            }
        }

        protected override bool RetryFailedAsyncTask(Exception exception, CancellationToken cancellationToken, int retryCountRemaining, out TimeSpan retryDelay)
        {
            retryDelay = TimeSpan.FromMilliseconds(HttpErrorRetryTimeout);  // Never increasing timeout
            return retryCountRemaining > 0;
        }

        /// <summary>
        ///     Sends all the messages
        /// </summary>
        /// <param name="message"></param>
        private async Task SendFast(ArraySegment<byte> message)
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

            httpResponseMessage.EnsureSuccessStatusCode();  // Throw if not a success code and trigger retry
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
            if (_propertiesChanged.IsEmpty) return;

            lock (_propertiesChanged)
            {
                var oldHttpClient = _httpClient;
                _httpClient = null;
                oldHttpClient?.Dispose();

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
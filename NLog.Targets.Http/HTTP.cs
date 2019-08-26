using System;
using System.Net;
using NLog.Config;

namespace NLog.Targets.Http
{
    [Target("HTTP")]
    public class HTTP:TargetWithLayout
    {
        [RequiredParameter] 
        public string URL { get; set; }

        public string Authorization { get; set; }

        public bool IgnoreSslErrors { get; set; } = true;
 
        protected override void Write(LogEventInfo logEvent) 
        { 
            string logMessage = this.Layout.Render(logEvent); 

            SendTheMessageToRemoteHost(this.URL, logMessage); 
        } 
 
        private void SendTheMessageToRemoteHost(string url, string message)
        {
            HttpWebRequest req = WebRequest.CreateHttp(url);
            
            //TODO
            //req.Proxy = new System.Net.WebProxy(ProxyString, true);

            if(IgnoreSslErrors)
            req.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
            
            //TODO
            req.ContentType = "application/json";
            req.KeepAlive = false;
            if (!String.IsNullOrWhiteSpace(Authorization))
            {
                req.Headers.Add("Authorization", Authorization);
            }
            req.Method = "POST";
            //We need to count how many bytes we're sending. Post'ed Faked Forms should be name=value&
            byte [] bytes = System.Text.Encoding.ASCII.GetBytes(message);
            req.ContentLength = bytes.Length;
            using (System.IO.Stream os = req.GetRequestStream())
            {
                os.Write (bytes, 0, bytes.Length); //Push it out there
                os.Close ();
            }

            req.GetResponseAsync().ContinueWith(resp =>
            {
                using (System.IO.StreamReader sr = new System.IO.StreamReader(resp.Result.GetResponseStream()))
                {
                    //TODO
                    var result = sr.ReadToEnd().Trim();
                    //Common.InternalLogger.Warn(ex, "SplunkHttpEventCollector: Failed to lookup {0}", lookupType);
                }
            });

        } 
    }
}

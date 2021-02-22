using System;
#if (NETCORE30 || NETSTANDARD21)
#endif

namespace NLog.Targets.Http
{
    /// <summary>
    /// The message that failed during flush.
    /// </summary>
    public sealed class FlushErrorEventArgs : EventArgs
    {
        internal FlushErrorEventArgs(string failedMessage)
        {
            FailedMessage = failedMessage;
        }

        /// <summary>
        /// The message that was supposed to be sent.
        /// </summary>
        public string FailedMessage { get; set; }
    }
}
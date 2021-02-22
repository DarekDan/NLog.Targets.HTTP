using System.Text;
using NLog.LayoutRenderers;

namespace NLog.Targets.Http
{
    // ReSharper disable once StringLiteralTypo
    [LayoutRenderer("unixtime")]
    public class UnixTimeLayoutRenderer : LayoutRenderer
    {
        /// <summary>
        /// Convert to UTC
        /// </summary>
        // ReSharper disable once MemberCanBePrivate.Global
        public bool UniversalTime { get; set; }

        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var cd = UniversalTime ? logEvent.TimeStamp.ToUniversalTime() : logEvent.TimeStamp;
            builder.Append($"{cd.Ticks / 10000000L - 62135596800L}.{cd.Millisecond:000}");
        }
    }
}
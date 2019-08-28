using System;
using System.Text;
using NLog.LayoutRenderers;

namespace NLog.Targets.Http
{
    [LayoutRenderer("unix-time")]
    public class UnixTimeLayoutRenderer:LayoutRenderer
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var cd = logEvent.TimeStamp;
            builder.Append($"{cd.Ticks / 10000000L - 62135596800L}.{cd.Millisecond}");
        }
    }
}

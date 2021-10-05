using NLog.Layouts;
using NLog.Config;

namespace NLog.Targets.Http
{
    [NLogConfigurationItem]
    public class NHttpHeader
    {
        public string Name { get; set; }
        public Layout Value { get; set; }
    }
}
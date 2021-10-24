using NLog.Config;
using NLog.Layouts;

namespace NLog.Targets.Http
{
    [NLogConfigurationItem]
    public class NHttpHeader
    {
        public string Name { get; set; }
        public Layout Value { get; set; }
    }
}
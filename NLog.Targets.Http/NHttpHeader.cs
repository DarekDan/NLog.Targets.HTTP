using NLog.Layouts;

namespace NLog.Targets.Http
{
    public class NHttpHeader
    {
        public string Name { get; set; }
        public Layout Value { get; set; }
    }
}
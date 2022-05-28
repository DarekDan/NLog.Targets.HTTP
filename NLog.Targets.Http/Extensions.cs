using System.IO;

namespace NLog.Targets.Http
{
    internal static class Extensions
    {
        public static void Append(this MemoryStream memoryStream, byte[] arrayBytes)
        {
            memoryStream.Write(arrayBytes, 0, arrayBytes.Length);
        }
    }
}
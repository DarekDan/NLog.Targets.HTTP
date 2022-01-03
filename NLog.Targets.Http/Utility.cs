using System.IO;
using System.IO.Compression;
using System.Text;

namespace NLog.Targets.Http
{
    internal class Utility
    {
        public static byte[] Zip(string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);

            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(mso, CompressionMode.Compress))
                {
                    msi.CopyTo(gs);
                }

                return mso.ToArray();
            }
        }

        public static string Unzip(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(msi, CompressionMode.Decompress))
                {
                    gs.CopyTo(mso);
                }

                return Encoding.UTF8.GetString(mso.ToArray());
            }
        }

        public static byte[] UnzipAsBytes(byte[] bytes)
        {
            using (var msInput = new MemoryStream(bytes))
            using (var msOutput = new MemoryStream())
            {
                using (var zipStream = new GZipStream(msInput, CompressionMode.Decompress))
                {
                    zipStream.CopyTo(msOutput);
                }

                return msOutput.ToArray();
            }
        }
    }
}
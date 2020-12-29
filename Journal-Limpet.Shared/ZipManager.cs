using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Journal_Limpet.Shared
{
    public class ZipManager
    {
        public static byte[] Zip(string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);

            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(mso, CompressionMode.Compress))
                {
                    CopyTo(msi, gs);
                }

                return mso.ToArray();
            }
        }

        public static string Unzip(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                if (IsPossiblyGZippedBytes(bytes))
                {
                    using (var gs = new GZipStream(msi, CompressionMode.Decompress))
                    {
                        CopyTo(gs, mso);
                    }

                    return Encoding.UTF8.GetString(mso.ToArray());
                }

                return Encoding.UTF8.GetString(bytes);
            }
        }

        private static void CopyTo(Stream src, Stream dest)
        {
            byte[] bytes = new byte[4096];

            int cnt;

            while ((cnt = src.Read(bytes, 0, bytes.Length)) != 0)
            {
                dest.Write(bytes, 0, cnt);
            }
        }

        private static byte[] GZipHeaderBytes = { 0x1f, 0x8b, 8 };

        private static bool IsPossiblyGZippedBytes(byte[] a)
        {
            var yes = a.Length > 10;

            if (!yes)
            {
                return false;
            }

            var header = a.Take(3);

            return header.SequenceEqual(GZipHeaderBytes);
        }
    }
}

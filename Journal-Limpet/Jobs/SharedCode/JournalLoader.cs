using Journal_Limpet.Shared;
using Journal_Limpet.Shared.Models.Journal;
using Minio;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Journal_Limpet.Jobs.SharedCode
{
    public static class JournalLoader
    {
        public static async Task<string[]> LoadJournal(MinioClient _minioClient, UserJournal journalItem, MemoryStream outFile)
        {
            var stats = await _minioClient.StatObjectAsync("journal-limpet", journalItem.S3Path);

            await _minioClient.GetObjectAsync("journal-limpet", journalItem.S3Path,
                0, stats.Size,
                cb =>
                {
                    cb.CopyTo(outFile);
                }
            );

            outFile.Seek(0, SeekOrigin.Begin);

            var journalContent = ZipManager.Unzip(outFile.ToArray());
            var journalRows = journalContent.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);

            return journalRows;
        }
    }
}

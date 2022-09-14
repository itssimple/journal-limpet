using Journal_Limpet.Shared;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models.Journal;
using Microsoft.Data.SqlClient;
using Minio;
using Minio.Exceptions;
using System;
using System.IO;
using System.Security.Claims;
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

        public static async Task<(string fileName, string journalContent)> GetJournalForDate(MSSQLDB _db, MinioClient _minioClient, ClaimsPrincipal user, DateTime journalDate)
        {
            var f = "CAPIJournal." +
                       journalDate.Year.ToString().Substring(2) +
                       journalDate.Month.ToString().PadLeft(2, '0') +
                       journalDate.Day.ToString().PadLeft(2, '0') +
                       journalDate.Hour.ToString().PadLeft(2, '0') +
                       journalDate.Minute.ToString().PadLeft(2, '0') +
                       journalDate.Second.ToString().PadLeft(2, '0') +
                       ".01.log";

            var journalItem = await _db.ExecuteSingleRowAsync<UserJournal>(
                "SELECT * FROM user_journal WHERE user_identifier = @user_identifier AND journal_date = @journal_date",
                new SqlParameter("user_identifier", user.Identity.Name),
                new SqlParameter("journal_date", journalDate.Date)
            );

            if (journalItem == null)
                return (null, null);

            using (MemoryStream outFile = new MemoryStream())
            {
                var journalIdentifier = journalItem.S3Path;

                try
                {
                    var stats = await _minioClient.StatObjectAsync("journal-limpet", journalIdentifier);

                    await _minioClient.GetObjectAsync("journal-limpet", journalIdentifier,
                        0, stats.Size,
                        cb =>
                        {
                            cb.CopyTo(outFile);
                        }
                    );

                    outFile.Seek(0, SeekOrigin.Begin);

                    var journalContent = ZipManager.Unzip(outFile.ToArray());

                    return (f, journalContent);
                }
                catch (ObjectNotFoundException)
                {
                    return (null, null);
                }
            }
        }
    }
}

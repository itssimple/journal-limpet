using Journal_Limpet.Shared;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models.Journal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Minio;
using Minio.Exceptions;
using System.IO;
using System.Threading.Tasks;

namespace Journal_Limpet.Pages
{
    public class ViewJournalModel : PageModel
    {
        private readonly MSSQLDB _db;
        private readonly MinioClient _minioClient;

        public UserJournal JournalItem { get; set; }
        public string JournalContent { get; set; }

        public ViewJournalModel(MSSQLDB db, MinioClient minioClient)
        {
            _db = db;
            _minioClient = minioClient;
        }

        public async Task<IActionResult> OnGetAsync(long journal_id)
        {
            var journalItem = await _db.ExecuteSingleRowAsync<UserJournal>(
                "select * from user_journal where journal_id = @journal_id AND last_processed_line_number > 0 AND user_identifier = @user_identifier",
                new SqlParameter("user_identifier", User.Identity.Name),
                new SqlParameter("journal_id", journal_id)
            );

            if (journalItem == null)
            {
                return NotFound();
            }

            JournalItem = journalItem;
            JournalContent = await GetJournalContent(journalItem);

            return Page();
        }

        async Task<string> GetJournalContent(UserJournal journalItem)
        {
            if (journalItem == null)
                return null;

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

                    return journalContent;
                }
                catch (ObjectNotFoundException)
                {
                    return null;
                }
            }
        }
    }
}

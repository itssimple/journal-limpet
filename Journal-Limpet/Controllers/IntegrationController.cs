using Journal_Limpet.Jobs.SharedCode;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models.Journal;
using Journal_Limpet.Shared.Models.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Minio;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Journal_Limpet.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = "APITokenAuthentication")]
    public class IntegrationController : ControllerBase
    {
        private readonly MSSQLDB _db;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _memoryCache;
        private readonly MinioClient _minioClient;
        private readonly IHttpClientFactory _httpClientFactory;

        public IntegrationController(MSSQLDB db, IConfiguration configuration, IMemoryCache memoryCache, MinioClient minioClient, IHttpClientFactory httpClientFactory)
        {
            _db = db;
            _configuration = configuration;
            _memoryCache = memoryCache;
            _minioClient = minioClient;
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet("info")]
        public async Task<JsonResult> GetInfo()
        {
            var user = (await _db.ExecuteListAsync<Profile>(
                "SELECT * FROM user_profile WHERE user_identifier = @id",
                new SqlParameter("@id", Guid.Parse(User.Identity.Name))))
                .FirstOrDefault();

            if (user != null)
            {
                // This UUID has a user!
                return new JsonResult(user);
            }

            return new JsonResult(new
            {
                success = false,
                error = "Missing account"
            });
        }

        [HttpGet("all-journals/download")]
        public async Task<IActionResult> DownloadAllJournalsAsync()
        {
            var allUserJournals = await _db.ExecuteListAsync<UserJournal>("SELECT * FROM user_journal WHERE user_identifier = @user_identifier AND last_processed_line_number > 0 ORDER BY journal_date ASC",
                new SqlParameter("user_identifier", User.Identity.Name));

            byte[] outBytes;

            using (var ms = new MemoryStream())
            {
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (var journal in allUserJournals)
                    {
                        var journalData = await JournalLoader.GetJournalForDate(_db, _minioClient, User, journal.JournalDate.Date);
                        var fileEntry = archive.CreateEntry(journalData.fileName);

                        using var fs = fileEntry.Open();
                        using var sw = new StreamWriter(fs);
                        {
                            await sw.WriteAsync(journalData.journalContent);
                        }
                    }
                }
                ms.Seek(0, SeekOrigin.Begin);

                outBytes = ms.ToArray();
            }

            return File(outBytes, "application/zip", $"JL-JournalBackup-{DateTime.Now.Date.ToShortDateString()}.zip");
        }
    }
}

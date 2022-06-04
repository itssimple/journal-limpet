using Hangfire;
using Journal_Limpet.Jobs;
using Journal_Limpet.Shared;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models;
using Journal_Limpet.Shared.Models.Journal;
using Journal_Limpet.Shared.Models.User;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Minio;
using Minio.Exceptions;
using StackExchange.Exceptional;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Journal_Limpet.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class JournalController : ControllerBase
    {
        private readonly MSSQLDB _db;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _memoryCache;
        private readonly MinioClient _minioClient;

        public JournalController(MSSQLDB db, IConfiguration configuration, IMemoryCache memoryCache, MinioClient minioClient)
        {
            _db = db;
            _configuration = configuration;
            _memoryCache = memoryCache;
            _minioClient = minioClient;
        }

        [HttpGet("info")]
        public async Task<JsonResult> GetInfo([FromQuery] string uuid)
        {
            var user = (await _db.ExecuteListAsync<Profile>(
                "SELECT * FROM user_profile WHERE user_identifier = @id",
                new SqlParameter("@id", Guid.Parse(uuid))))
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

        [HttpGet("authenticate")]
        [AllowAnonymous]
        public async Task<IActionResult> Authenticate()
        {
            var code = Request.Query["code"];
            var state = Request.Query["state"];

            if (!_memoryCache.TryGetValue("frontierLogin-" + HttpContext.Connection.RemoteIpAddress.ToString(), out string storedState))
            {
                return BadRequest("Could not find login token, try again");
            }

            if (state != storedState)
            {
                return Unauthorized("Invalid state, please relogin");
            }

            var redirectUrl = string.Format("{0}://{1}{2}", Request.Scheme, Request.Host, Url.Content("~/api/journal/authenticate"));

            using var c = new HttpClient();

            var formData = new Dictionary<string, string>();
            formData.Add("grant_type", "authorization_code");
            formData.Add("code", code);
            formData.Add("client_id", _configuration["EliteDangerous:ClientId"]);
            formData.Add("client_secret", _configuration["EliteDangerous:ClientSecret"]);
            formData.Add("state", state);
            formData.Add("redirect_uri", redirectUrl);

            var result = await c.PostAsync("https://auth.frontierstore.net/token", new FormUrlEncodedContent(formData));

            var tokenInfo = JsonSerializer.Deserialize<OAuth2Response>(await result.Content.ReadAsStringAsync());

            if (result.IsSuccessStatusCode)
            {
                c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenInfo.AccessToken);

                result = await c.GetAsync("https://auth.frontierstore.net/me");

                var profile = JsonSerializer.Deserialize<FrontierProfile>(await result.Content.ReadAsStringAsync());

                var settings = new Settings
                {
                    AuthToken = tokenInfo.AccessToken,
                    TokenExpiration = DateTimeOffset.UtcNow.AddSeconds(tokenInfo.ExpiresIn),
                    RefreshToken = tokenInfo.RefreshToken,
                    FrontierProfile = profile
                };

                // Move this so a service later
                var matchingUser = (await _db.ExecuteListAsync<Profile>(@"
SELECT *
FROM user_profile
WHERE JSON_VALUE(user_settings, '$.FrontierProfile.customer_id') = @customerId",
new SqlParameter("customerId", profile.CustomerId))
                ).FirstOrDefault();

                if (matchingUser != null)
                {
                    // Update user with new token info
                    await _db.ExecuteNonQueryAsync("UPDATE user_profile SET user_settings = @settings, last_notification_mail = NULL, skip_download = 0 WHERE user_identifier = @userIdentifier",
                        new SqlParameter("settings", JsonSerializer.Serialize(settings)),
                        new SqlParameter("userIdentifier", matchingUser.UserIdentifier)
                    );

                    matchingUser.UserSettings = settings;
                }
                else
                {
                    // Create new user
                    matchingUser = await _db.ExecuteSingleRowAsync<Profile>("INSERT INTO user_profile (user_settings) OUTPUT INSERTED.* VALUES (@settings)",
                        new SqlParameter("settings", JsonSerializer.Serialize(settings))
                    );

                    var userCount = await _db.ExecuteScalarAsync<long>("SELECT COUNT_BIG(user_identifier) FROM user_profile WHERE deleted = 0");

                    await SSEActivitySender.SendGlobalActivityAsync("A new user has registered!", $"We now have {userCount:N0} users registered!");
                    await SSEActivitySender.SendStatsActivityAsync(_db);

                    BackgroundJob.Enqueue(() => JournalDownloader.DownloadJournalAsync(matchingUser.UserIdentifier, null));
                }

                var claims = new List<Claim>()
                {
                    new Claim(ClaimTypes.Name, matchingUser.UserIdentifier.ToString())
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                var authProperties = new AuthenticationProperties
                {
                    AllowRefresh = true,
                    IsPersistent = false,
                    IssuedUtc = DateTimeOffset.UtcNow
                };

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity), authProperties);

                return LocalRedirect("~/Index");
            }
            else
            {
                return new JsonResult(await result.Content.ReadAsStringAsync());
            }
        }

        [HttpGet("{journalDate}/download")]
        public async Task<IActionResult> DownloadJournalAsync(DateTime journalDate)
        {
            var journal = await GetJournalForDate(journalDate);

            if (journal.fileName == null)
                return NotFound();

            MemoryStream unzippedContent = new MemoryStream(Encoding.UTF8.GetBytes(journal.journalContent));
            return File(unzippedContent, "application/octet-stream", journal.fileName);
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
                        var journalData = await GetJournalForDate(journal.JournalDate.Date);
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

        async Task<(string fileName, string journalContent)> GetJournalForDate(DateTime journalDate)
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
                new SqlParameter("user_identifier", User.Identity.Name),
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

        [HttpGet("logout")]
        public async Task<IActionResult> SignOut()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return LocalRedirect("~/Index");
        }

        [Route("exceptions/{path?}/{subPath?}")]
        public async Task Exceptions()
        {
            if (User.Identity.IsAuthenticated && User.Identity.Name.Equals(_configuration["ExceptionUserId"], StringComparison.InvariantCultureIgnoreCase))
            {
                await ExceptionalMiddleware.HandleRequestAsync(HttpContext).ConfigureAwait(false);
            }
        }
    }
}

using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models.API.Profile;
using Journal_Limpet.Shared.Models.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Journal_Limpet.Pages
{
    [AllowAnonymous]
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly MSSQLDB _db;
        private readonly IHttpClientFactory _httpClientFactory;
        public long UserCount = 0;
        public string CommanderName;
        public long LoggedInUserJournalCount = 0;
        public long TotalUserJournalCount = 0;
        public long TotalUserJournalLines = 0;

        public Dictionary<string, bool> IntegrationsEnabled = new Dictionary<string, bool>();

        public Profile LoggedInUser { get; set; }

        public IndexModel(ILogger<IndexModel> logger, MSSQLDB db, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _db = db;
            _httpClientFactory = httpClientFactory;
        }

        public async Task OnGet()
        {
            var mod = await SharedSettings.GetIndexStatsAsync(_db);

            TotalUserJournalCount = mod.TotalUserJournalCount;
            TotalUserJournalLines = mod.TotalUserJournalLines;
            UserCount = mod.TotalUserCount;

            if (User.Identity.IsAuthenticated)
            {
                LoggedInUser = await _db.ExecuteSingleRowAsync<Profile>("SELECT * FROM user_profile WHERE user_identifier = @user_identifier", new SqlParameter("user_identifier", User.Identity.Name));

                var hc = _httpClientFactory.CreateClient();
                hc.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", LoggedInUser.UserSettings.AuthToken);
                hc.BaseAddress = new Uri("https://companion.orerve.net");

                var cmdrProfile = await GetProfileAsync(hc);
                var cmdrJson = await cmdrProfile.Content.ReadAsStringAsync();

                if (!cmdrProfile.IsSuccessStatusCode || cmdrJson.Trim() == "{}")
                {
                    Redirect("~/api/journal/logout");
                    return;
                }

                var cmdrInfo = JsonSerializer.Deserialize<EliteProfile>(cmdrJson);

                CommanderName = cmdrInfo.Commander.Name;

                LoggedInUserJournalCount = await _db.ExecuteScalarAsync<int>(
                  "SELECT COUNT(journal_id) FROM user_journal WHERE user_identifier = @user_identifier AND last_processed_line_number > 0",
                  new SqlParameter("user_identifier", User.Identity.Name)
                );

                IntegrationsEnabled.Add("EDDN", LoggedInUser.SendToEDDN);

                foreach (var inte in LoggedInUser.IntegrationSettings)
                {
                    IntegrationsEnabled.Add(inte.Key, inte.Value.GetProperty("enabled").GetBoolean());
                }
            }
        }

        private async Task<HttpResponseMessage> GetProfileAsync(HttpClient hc)
        {
            return await hc.GetAsync($"/profile");
        }
    }
}

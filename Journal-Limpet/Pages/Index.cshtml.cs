using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Journal_Limpet.Pages
{
    [AllowAnonymous]
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly MSSQLDB _db;

        public long UserCount = 0;

        public long LoggedInUserJournalCount = 0;
        public long TotalUserJournalCount = 0;
        public long TotalUserJournalLines = 0;

        public Dictionary<string, bool> IntegrationsEnabled = new Dictionary<string, bool>();

        public Profile LoggedInUser { get; set; }

        public IndexModel(ILogger<IndexModel> logger, MSSQLDB db)
        {
            _logger = logger;
            _db = db;
        }

        public async Task OnGet()
        {
            var mod = await SharedSettings.GetIndexStatsAsync(_db);

            TotalUserJournalCount = mod.TotalUserJournalCount;
            TotalUserJournalLines = mod.TotalUserJournalLines;
            UserCount = mod.TotalUserCount;

            if (User.Identity.IsAuthenticated)
            {
                LoggedInUserJournalCount = await _db.ExecuteScalarAsync<int>(
                    "SELECT COUNT(journal_id) FROM user_journal WHERE user_identifier = @user_identifier AND last_processed_line_number > 0",
                    new SqlParameter("user_identifier", User.Identity.Name)
                );

                LoggedInUser = await _db.ExecuteSingleRowAsync<Profile>("SELECT * FROM user_profile WHERE user_identifier = @user_identifier", new SqlParameter("user_identifier", User.Identity.Name));

                IntegrationsEnabled.Add("EDDN", LoggedInUser.SendToEDDN);

                foreach (var inte in LoggedInUser.IntegrationSettings)
                {
                    if (inte.Key == "Canonn R&D" && !inte.Value.GetProperty("enabled").GetBoolean())
                    {
                        continue;
                    }

                    IntegrationsEnabled.Add(inte.Key, inte.Value.GetProperty("enabled").GetBoolean());
                }
            }
        }
    }
}

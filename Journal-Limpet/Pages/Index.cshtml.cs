using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models;
using Journal_Limpet.Shared.Models.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
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

        public Profile LoggedInUser { get; set; }

        public IndexModel(ILogger<IndexModel> logger, MSSQLDB db)
        {
            _logger = logger;
            _db = db;
        }

        public async Task OnGet()
        {
            var mod = await _db.ExecuteSingleRowAsync<IndexStatsModel>(@"SELECT CAST(COUNT(DISTINCT up.user_identifier) AS bigint) user_count, 
CAST(COUNT(journal_id) AS bigint) journal_count,
CAST(SUM(last_processed_line_number) AS bigint) total_number_of_lines
FROM user_profile up
LEFT JOIN user_journal uj ON up.user_identifier = uj.user_identifier AND uj.last_processed_line_number > 0
WHERE up.deleted = 0");

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
            }
        }
    }
}

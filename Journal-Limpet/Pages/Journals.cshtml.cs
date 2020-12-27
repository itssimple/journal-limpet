using Journal_Limpet.Shared.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Journal_Limpet.Pages
{
    [Authorize]
    public class JournalsModel : PageModel
    {
        private readonly MSSQLDB _db;
        public List<JournalListItem> JournalItems { get; set; }

        public JournalsModel(MSSQLDB db)
        {
            _db = db;
        }

        public async Task OnGetAsync()
        {
            JournalItems = await _db.ExecuteListAsync<JournalListItem>("select * from user_journal where last_processed_line_number > 0 AND user_identifier = @user_identifier ORDER BY journal_date DESC", new SqlParameter("user_identifier", User.Identity.Name));
        }

        public class JournalListItem
        {
            public int JournalId { get; set; }
            public string JournalDate { get; set; }
            public string CompleteEntry { get; set; }
            public int NumberOfLines { get; set; }

            public JournalListItem(DataRow row)
            {
                JournalId = row.Field<int>("journal_id");
                JournalDate = new DateTimeOffset(row.Field<DateTime>("journal_date"), TimeSpan.Zero).Date.ToString("yyyy-MM-dd");
                CompleteEntry = row.Field<bool>("complete_entry") ? "Yes" : "No";
                NumberOfLines = row.Field<int>("last_processed_line_number");
            }
        }
    }
}

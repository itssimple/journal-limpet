using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models.Journal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
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
    }
}

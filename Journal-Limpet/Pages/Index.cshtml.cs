using Journal_Limpet.Shared.Database;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Journal_Limpet.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly NPGDB _db;

        public long UserCount = 0;

        public IndexModel(ILogger<IndexModel> logger, NPGDB db)
        {
            _logger = logger;
            _db = db;
        }

        public async Task OnGet()
        {
            UserCount = await _db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM user_profile WHERE deleted = false");
        }
    }
}

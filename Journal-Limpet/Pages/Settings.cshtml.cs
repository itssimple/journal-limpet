using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;

namespace Journal_Limpet.Pages
{
    [Authorize]
    public class SettingsModel : PageModel
    {
        private readonly MSSQLDB _db;

        [BindProperty]
        public string NotificationEmail { get; set; }

        public SettingsModel(MSSQLDB db)
        {
            _db = db;
        }

        public async Task OnGet()
        {
            var profile = await _db.ExecuteSingleRowAsync<Profile>("SELECT * FROM user_profile WHERE user_identifier = @user_identifier", new SqlParameter("user_identifier", User.Identity.Name));
            NotificationEmail = profile.NotificationEmail;
        }

        public async Task OnPost()
        {
            await _db.ExecuteNonQueryAsync(
                "UPDATE user_profile SET notification_email = @notification_email WHERE user_identifier = @user_identifier",
                new SqlParameter("user_identifier", User.Identity.Name),
                new SqlParameter("notification_email", NotificationEmail)
            );
        }
    }
}

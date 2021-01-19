using Journal_Limpet.Shared;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using System.Threading.Tasks;

namespace Journal_Limpet.Pages
{
    [Authorize]
    public class SettingsModel : PageModel
    {
        private readonly MSSQLDB _db;

        [BindProperty]
        public string NotificationEmail { get; set; }
        [BindProperty]
        public EDSMIntegrationSettings EDSM { get; set; }

        [BindProperty]
        public bool EDDNEnabled { get; set; }

        public SettingsModel(MSSQLDB db)
        {
            _db = db;
        }

        public async Task OnGetAsync()
        {
            var profile = await _db.ExecuteSingleRowAsync<Profile>("SELECT * FROM user_profile WHERE user_identifier = @user_identifier", new SqlParameter("user_identifier", User.Identity.Name));
            NotificationEmail = profile.NotificationEmail;

            EDDNEnabled = profile.SendToEDDN;

            if (profile.IntegrationSettings.ContainsKey("EDSM"))
            {
                var edsmSettings = profile.IntegrationSettings["EDSM"].GetTypedObject<EDSMIntegrationSettings>();
                EDSM = edsmSettings;
            }
        }

        public async Task OnPostAsync()
        {
            var profile = await _db.ExecuteSingleRowAsync<Profile>("SELECT * FROM user_profile WHERE user_identifier = @user_identifier", new SqlParameter("user_identifier", User.Identity.Name));

            var integrationSettings = profile.IntegrationSettings;
            integrationSettings["EDSM"] = EDSM.AsJsonElement();

            var integrationJson = JsonSerializer.Serialize(integrationSettings);

            await _db.ExecuteNonQueryAsync(
                "UPDATE user_profile SET notification_email = @notification_email, integration_settings = @integration_settings, send_to_eddn = @send_to_eddn WHERE user_identifier = @user_identifier",
                new SqlParameter("user_identifier", User.Identity.Name),
                new SqlParameter("notification_email", NotificationEmail ?? string.Empty),
                new SqlParameter("integration_settings", integrationJson),
                new SqlParameter("send_to_eddn", EDDNEnabled)
            );
        }
    }
}

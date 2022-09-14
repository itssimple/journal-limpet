using Journal_Limpet.Shared;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Threading.Tasks;

namespace Journal_Limpet.Pages
{
    [Authorize]
    public class SettingsModel : PageModel
    {
        private readonly MSSQLDB _db;
        IConfiguration _configuration;

        [BindProperty]
        public string NotificationEmail { get; set; }

        [BindProperty]
        public EDSMIntegrationSettings EDSM { get; set; }
        [BindProperty]
        public CanonnRDIntegrationSettings CanonnRD { get; set; }

        [BindProperty]
        public bool EDDNEnabled { get; set; }

        [BindProperty]
        public string JournalLimpetAPIToken { get; set; }

        public SettingsModel(MSSQLDB db, IConfiguration configuration)
        {
            _db = db;
            _configuration = configuration;
        }

        public async Task OnGetAsync()
        {
            var profile = await _db.ExecuteSingleRowAsync<Profile>("SELECT * FROM user_profile WHERE user_identifier = @user_identifier", new SqlParameter("user_identifier", User.Identity.Name));
            NotificationEmail = profile.NotificationEmail;

            EDDNEnabled = profile.SendToEDDN;

            JournalLimpetAPIToken = profile.UserSettings.JournalLimpetAPIToken;

            if (profile.IntegrationSettings.ContainsKey("EDSM"))
            {
                var edsmSettings = profile.IntegrationSettings["EDSM"].GetTypedObject<EDSMIntegrationSettings>();
                EDSM = edsmSettings;
            }

            if (profile.IntegrationSettings.ContainsKey("Canonn R&D"))
            {
                var canonnRDSettings = profile.IntegrationSettings["Canonn R&D"].GetTypedObject<CanonnRDIntegrationSettings>();
                CanonnRD = canonnRDSettings;
            }
        }

        public async Task OnPostAsync()
        {
            var profile = await _db.ExecuteSingleRowAsync<Profile>("SELECT * FROM user_profile WHERE user_identifier = @user_identifier", new SqlParameter("user_identifier", User.Identity.Name));

            var integrationSettings = profile.IntegrationSettings;
            integrationSettings["EDSM"] = EDSM.AsJsonElement();
            integrationSettings["Canonn R&D"] = CanonnRD.AsJsonElement();

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

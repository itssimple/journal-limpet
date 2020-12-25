using Hangfire.Console;
using Hangfire.Server;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models;
using Journal_Limpet.Shared.Models.User;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SendGrid;
using SendGrid.Helpers.Mail;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Journal_Limpet.Jobs
{
    public class AccountTokenRefresher
    {
        public static async Task RefreshUserTokensAsync(PerformContext context)
        {
            context.WriteLine("Looking for tokens to refresh!");
            using (var scope = Startup.ServiceProvider.CreateScope())
            {
                MSSQLDB db = scope.ServiceProvider.GetRequiredService<MSSQLDB>();

                IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                var soonExpiringUsers = await db.ExecuteListAsync<Shared.Models.User.Profile>(
    @"SELECT *
FROM user_profile
WHERE CAST(JSON_VALUE(user_settings, '$.TokenExpiration') as DATETIMEOFFSET) < DATEADD(HOUR, 2, GETUTCDATE())
AND last_notification_mail IS NULL"
                );
                context.WriteLine($"Found {soonExpiringUsers.Count} user(s) to refresh tokens for");

                IHttpClientFactory _hcf = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

                var hc = _hcf.CreateClient();

                foreach (var user in soonExpiringUsers)
                {
                    var res = await hc.PostAsync("https://auth.frontierstore.net/token",
                        new FormUrlEncodedContent(new Dictionary<string, string>
                        {
                            { "grant_type", "refresh_token" },
                            { "refresh_token", user.UserSettings.RefreshToken.ToString() },
                            { "client_id", configuration["EliteDangerous:ClientId"] }
                        })
                    );

                    if (!res.IsSuccessStatusCode)
                    {
                        // The user is not authorized to perform more automatic refreshes of the token
                        // Send notification to the user that they need to re-login if they want to keep getting their journals stored

                        if (!string.IsNullOrWhiteSpace(user.NotificationEmail))
                        {
                            var sendgridClient = new SendGridClient(configuration["SendGrid:ApiKey"]);
                            var mail = MailHelper.CreateSingleEmail(
                                new EmailAddress("no-reply+account-notifications@journal-limpet.com", "Journal Limpet"),
                                new EmailAddress(user.NotificationEmail),
                                "Login needed for further journal storage",
    @"Hi there!

This is an automated email, since you have logged in to Journal Limpet at least once.

I'm sorry that I have to send you this email, but we need you to log in to Journal-Limpet <https://journal-limpet.com> again.

.. at least if you want us to be able to continue:
- Storing your Elite: Dangerous journals
- Sync progress with other applications

And if you don't want us to sync your account any longer, we'll delete your account after 6 months from your last fetched journal.

This is the only email we will ever send you (every time that you need to login)

Regards,
NoLifeKing85
Journal Limpet",
    @"<html>
<body>
Hi there!<br />
<br />
This is an automated email, since you have logged in to Journal Limpet at least once.<br />
<br />
I'm sorry that I have to send you this email, but we need you to log in to <a href=""https://journal-limpet.com"" target=""_blank"">Journal-Limpet</a> again.<br />
<br />
.. at least if you want us to be able to continue:<br />
- Storing your Elite: Dangerous journals<br />
- Sync progress with other applications<br />
<br />
And if you don't want us to sync your account any longer, we'll delete your account after 6 months from your last fetched journal.<br />
<br />
This is the only email we will ever send you (every time that you need to login)<br />
<br />
Regards,<br />
NoLifeKing85<br />
Journal Limpet
</body>
</html>"
                    );

                            await sendgridClient.SendEmailAsync(mail);

                            await db.ExecuteNonQueryAsync("UPDATE user_profile SET last_notification_mail = GETUTCDATE() WHERE user_identifier = @userIdentifier",
                                new SqlParameter("@userIdentifier", user.UserIdentifier)
                            );
                        }
                    }
                    else
                    {
                        // We managed to grab a new token, lets save it!

                        var tokenInfo = JsonSerializer.Deserialize<OAuth2Response>(await res.Content.ReadAsStringAsync());

                        var settings = new Settings
                        {
                            AuthToken = tokenInfo.AccessToken,
                            TokenExpiration = DateTimeOffset.UtcNow.AddSeconds(tokenInfo.ExpiresIn),
                            RefreshToken = tokenInfo.RefreshToken,
                            FrontierProfile = user.UserSettings.FrontierProfile
                        };

                        // Update user with new token info
                        await db.ExecuteNonQueryAsync("UPDATE user_profile SET user_settings = @settings, last_notification_mail = NULL WHERE user_identifier = @userIdentifier",
                                new SqlParameter("@settings", JsonSerializer.Serialize(settings)),
                                new SqlParameter("@userIdentifier", user.UserIdentifier)
                            );
                    }
                }
            }
        }
    }
}

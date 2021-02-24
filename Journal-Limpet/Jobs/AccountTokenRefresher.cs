using Hangfire.Console;
using Hangfire.Server;
using Journal_Limpet.Jobs.SharedCode;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models;
using Journal_Limpet.Shared.Models.User;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
WHERE DATEDIFF(MINUTE, GETUTCDATE(), CAST(JSON_VALUE(user_settings, '$.TokenExpiration') as DATETIMEOFFSET)) < 60
AND last_notification_mail IS NULL
AND deleted = 0"
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
                            await SendLoginNotificationMethod.SendLoginNotification(db, configuration, user);
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

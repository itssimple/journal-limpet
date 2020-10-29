using Hangfire.Console;
using Hangfire.Server;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models;
using Journal_Limpet.Shared.Models.User;
using Microsoft.Extensions.Configuration;
using NpgsqlTypes;
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
            NPGDB db = Startup.ServiceProvider.GetService(typeof(NPGDB)) as NPGDB;

            IConfiguration configuration = Startup.ServiceProvider.GetService(typeof(IConfiguration)) as IConfiguration;

            var soonExpiringUsers = await db.ExecuteListAsync<Shared.Models.User.Profile>("SELECT * FROM user_profile WHERE CAST(CAST(user_settings->'TokenExpiration' as text) as timestamptz) < now() - INTERVAL '1 hour'");
            context.WriteLine($"Found {soonExpiringUsers.Count} user(s) to refresh tokens for");

            HttpClient hc = Startup.ServiceProvider.GetService(typeof(HttpClient)) as HttpClient;

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

                var tokenInfo = JsonSerializer.Deserialize<OAuth2Response>(await res.Content.ReadAsStringAsync());

                var settings = new Settings
                {
                    AuthToken = tokenInfo.AccessToken,
                    TokenExpiration = DateTimeOffset.UtcNow.AddSeconds(tokenInfo.ExpiresIn),
                    RefreshToken = tokenInfo.RefreshToken,
                    FrontierProfile = user.UserSettings.FrontierProfile
                };

                // Update user with new token info
                await db.ExecuteNonQueryAsync("UPDATE user_profile SET user_settings = @settings WHERE user_identifier = @userIdentifier",
                        new Npgsql.NpgsqlParameter("settings", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(settings) },
                        new Npgsql.NpgsqlParameter("userIdentifier", user.UserIdentifier)
                    );
            }
        }
    }
}

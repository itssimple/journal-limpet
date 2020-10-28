using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models;
using Journal_Limpet.Shared.Models.User;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Journal_Limpet.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JournalController : ControllerBase
    {
        private readonly NPGDB _db;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _memoryCache;

        public JournalController(NPGDB db, IConfiguration configuration, IMemoryCache memoryCache)
        {
            _db = db;
            _configuration = configuration;
            _memoryCache = memoryCache;
        }

        [HttpGet("info")]
        public async Task<JsonResult> GetInfo([FromQuery] string uuid)
        {
            var user = (await _db.ExecuteListAsync<Shared.Models.User.Profile>("SELECT * FROM user_profile WHERE user_identifier = @id", new Npgsql.NpgsqlParameter("id", Guid.Parse(uuid)))).FirstOrDefault();
            if (user != null)
            {
                // This UUID has a user!
                return new JsonResult(user);
            }

            return new JsonResult(new
            {
                success = false,
                error = "Missing account"
            });
        }

        [HttpGet("authenticate")]
        public async Task<IActionResult> Authenticate()
        {
            var code = Request.Query["code"];
            var state = Request.Query["state"];

            if (!_memoryCache.TryGetValue("frontierLogin-" + HttpContext.Connection.RemoteIpAddress.ToString(), out string storedState))
            {
                return BadRequest("Could not find login token, try again");
            }

            if (state != storedState)
            {
                return Unauthorized("Invalid state, please relogin");
            }

            var redirectUrl = string.Format("{0}://{1}{2}", Request.Scheme, Request.Host, Url.Content("~/api/journal/authenticate"));

            using var c = new HttpClient();

            var formData = new Dictionary<string, string>();
            formData.Add("grant_type", "authorization_code");
            formData.Add("code", code);
            formData.Add("client_id", _configuration["EliteDangerous:ClientId"]);
            formData.Add("client_secret", _configuration["EliteDangerous:ClientSecret"]);
            formData.Add("state", state);
            formData.Add("redirect_uri", redirectUrl);

            var result = await c.PostAsync("https://auth.frontierstore.net/token", new FormUrlEncodedContent(formData));

            var tokenInfo = JsonSerializer.Deserialize<OAuth2Response>(await result.Content.ReadAsStringAsync());

            if (result.IsSuccessStatusCode)
            {
                c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenInfo.AccessToken);

                result = await c.GetAsync("https://auth.frontierstore.net/me");

                var profile = JsonSerializer.Deserialize<FrontierProfile>(await result.Content.ReadAsStringAsync());

                var settings = new Settings
                {
                    AuthToken = tokenInfo.AccessToken,
                    TokenExpiration = DateTimeOffset.UtcNow.AddSeconds(tokenInfo.ExpiresIn),
                    RefreshToken = tokenInfo.RefreshToken,
                    FrontierProfile = profile
                };

                // Move this so a service later
                var matchingUser = (await _db.ExecuteListAsync<Shared.Models.User.Profile>("select * from user_profile WHERE CAST(user_settings->'FrontierProfile'->'CustomerId' AS text) = @customerId;", new Npgsql.NpgsqlParameter("customerId", profile.CustomerId))).FirstOrDefault();

                if (matchingUser != null)
                {
                    // Update user with new token info
                    await _db.ExecuteNonQueryAsync("UPDATE user_profile SET user_settings = @settings WHERE user_identifier = @userIdentifier",
                        new Npgsql.NpgsqlParameter("settings", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(settings) },
                        new Npgsql.NpgsqlParameter("userIdentifier", matchingUser.UserIdentifier)
                    );
                }
                else
                {
                    // Create new user
                    await _db.ExecuteNonQueryAsync("INSERT INTO user_profile (user_settings) VALUES (@settings)",
                        new Npgsql.NpgsqlParameter("settings", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(settings) }
                    );
                }

                // TODO: Save this, you dimwit.
                return new JsonResult(new { TokenInfo = tokenInfo, Profile = await result.Content.ReadAsStringAsync() });
            }
            else
            {
                return new JsonResult(await result.Content.ReadAsStringAsync());
            }
        }
    }
}

using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models;
using Journal_Limpet.Shared.Models.User;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

namespace Journal_Limpet.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class JournalController : ControllerBase
    {
        private readonly MSSQLDB _db;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _memoryCache;

        public JournalController(MSSQLDB db, IConfiguration configuration, IMemoryCache memoryCache)
        {
            _db = db;
            _configuration = configuration;
            _memoryCache = memoryCache;
        }

        [HttpGet("info")]
        public async Task<JsonResult> GetInfo([FromQuery] string uuid)
        {
            var user = (await _db.ExecuteListAsync<Shared.Models.User.Profile>(
                "SELECT * FROM user_profile WHERE user_identifier = @id",
                new SqlParameter("@id", Guid.Parse(uuid))))
                .FirstOrDefault();

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
        [AllowAnonymous]
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
                var matchingUser = (await _db.ExecuteListAsync<Shared.Models.User.Profile>(@"
SELECT *
FROM user_profile
WHERE JSON_VALUE(user_settings, '$.FrontierProfile.customer_id') = @customerId;",
new SqlParameter("@customerId", profile.CustomerId))
                ).FirstOrDefault();

                if (matchingUser != null)
                {
                    // Update user with new token info
                    await _db.ExecuteNonQueryAsync("UPDATE user_profile SET user_settings = @settings, last_notification_mail = NULL WHERE user_identifier = @userIdentifier",
                        new SqlParameter("@settings", JsonSerializer.Serialize(settings)),
                        new SqlParameter("@userIdentifier", matchingUser.UserIdentifier)
                    );

                    matchingUser.UserSettings = settings;
                }
                else
                {
                    // Create new user
                    matchingUser = (await _db.ExecuteListAsync<Shared.Models.User.Profile>("INSERT INTO user_profile (user_settings) OUTPUT INSERTED.* VALUES (@settings)",
                        new SqlParameter("@settings", JsonSerializer.Serialize(settings))
                    )).FirstOrDefault();
                }

                var claims = new List<Claim>()
                {
                    new Claim(ClaimTypes.Name, matchingUser.UserIdentifier.ToString())
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                var authProperties = new AuthenticationProperties
                {
                    AllowRefresh = true,
                    IsPersistent = false,
                    IssuedUtc = DateTimeOffset.UtcNow
                };

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity), authProperties);

                return LocalRedirect("~/Index");
            }
            else
            {
                return new JsonResult(await result.Content.ReadAsStringAsync());
            }
        }

        [HttpGet("logout")]
        public async Task<IActionResult> SignOut()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return LocalRedirect("~/Index");
        }
    }
}

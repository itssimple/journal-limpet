using Journal_Limpet.Shared.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Journal_Limpet.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JournalController : ControllerBase
    {
        private readonly NPGDB _db;
        private readonly IConfiguration _configuration;

        public JournalController(NPGDB db, IConfiguration configuration)
        {
            _db = db;
            _configuration = configuration;
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

            var tokenInfo = await result.Content.ReadAsStringAsync();

            if (result.IsSuccessStatusCode)
            {
                // TODO: Save this, you dimwit.
                return new JsonResult(tokenInfo);
            }
            else
            {

            }

            return new JsonResult(new
            {
                success = true
            });
        }
    }
}

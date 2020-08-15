using Journal_Limpet.Shared.Database;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Journal_Limpet.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JournalController : ControllerBase
    {
        private readonly NPGDB _db;

        public JournalController(NPGDB db)
        {
            _db = db;
        }

        [HttpGet("info")]
        public async Task<JsonResult> GetInfo([FromQuery] string uuid)
        {
            var user = (await _db.ExecuteListAsync<Shared.Models.UserProfile>("SELECT * FROM user_profile WHERE user_identifier = @id", new Npgsql.NpgsqlParameter("id", Guid.Parse(uuid)))).FirstOrDefault();
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
    }
}

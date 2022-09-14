using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models.User;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace Journal_Limpet
{
    public class APITokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        readonly MSSQLDB _db;
        /// <summary>
        /// Initializes a new instance of <see cref="APITokenAuthenticationHandler`1"/>.
        /// </summary>
        /// <param name="options">The monitor for the options instance.</param>
        /// <param name="logger">The <see cref="Microsoft.Extensions.Logging.ILoggerFactory"/>.</param>
        /// <param name="encoder">The <see cref="System.Text.Encodings.Web.UrlEncoder"/>.</param>
        /// <param name="clock">The <see cref="Microsoft.AspNetCore.Authentication.ISystemClock"/>.</param>
        public APITokenAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            MSSQLDB db
        ) : base(options, logger, encoder, clock)
        {
            _db = db;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.HttpContext.User.Identity.IsAuthenticated)
            {
                return await Task.FromResult(AuthenticateResult.NoResult());
            }

            var authHeader = Request.Headers.Authorization;

            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.ToString().StartsWith("bearer ", System.StringComparison.InvariantCultureIgnoreCase))
            {
                return await Task.FromResult(AuthenticateResult.Fail("Invalid/Missing API Token"));
            }

            var matchingUser = (await _db.ExecuteListAsync<Profile>(@"
SELECT *
FROM user_profile
WHERE JSON_VALUE(user_settings, '$.JournalLimpetAPIToken') = @apiToken",
new SqlParameter("apiToken", authHeader.ToString().Replace("bearer ", string.Empty, System.StringComparison.InvariantCultureIgnoreCase).Trim()))
                ).FirstOrDefault();

            if (matchingUser == null)
            {
                return await Task.FromResult(AuthenticateResult.Fail("Invalid API Token"));
            }

            var claims = new List<Claim>()
            {
                new Claim(ClaimTypes.Name, matchingUser.UserIdentifier.ToString())
            };

            var claimsIdentity = new ClaimsIdentity(claims, Scheme.Name);

            var principal = new ClaimsPrincipal(claimsIdentity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            var user = new GenericPrincipal(
                identity: claimsIdentity,
                roles: claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray()
            );

            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);
            Request.HttpContext.User = user;

            return await Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    public static class APITokenAuthenticationHandlerExtensions
    {
        public static AuthenticationBuilder AddAPITokenAuthenticationSchema(this AuthenticationBuilder builder)
        {
            builder.AddScheme<AuthenticationSchemeOptions, APITokenAuthenticationHandler>("APITokenAuthentication", null);
            return builder;
        }
    }
}

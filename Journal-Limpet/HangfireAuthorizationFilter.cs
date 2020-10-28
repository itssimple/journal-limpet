using Hangfire.Dashboard;
using System.Net;

namespace Journal_Limpet
{
    public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();

            var isLocalUser = httpContext.Connection.RemoteIpAddress == IPAddress.Loopback;

            var hasSecretKey = httpContext.Request.Query["hangFire"] == "temporary-secret-dashboard";

            return isLocalUser || hasSecretKey;
        }
    }
}
using Hangfire.Dashboard;
using System.Net;

namespace Journal_Limpet
{
    public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
    {
        private string _authKey;

        public HangfireAuthorizationFilter(string authKey)
        {
            _authKey = authKey;
        }

        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();

            var isLocalUser = httpContext.Connection.RemoteIpAddress == IPAddress.Loopback;

            var hasSecretKey = httpContext.Request.Query["hangFire"] == _authKey;

            return true; // isLocalUser || hasSecretKey;
        }
    }
}
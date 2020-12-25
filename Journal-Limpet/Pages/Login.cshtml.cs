using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System;

namespace Journal_Limpet.Pages
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _memoryCache;

        public LoginModel(IConfiguration configuration, IMemoryCache memoryCache)
        {
            _configuration = configuration;
            _memoryCache = memoryCache;
        }
        public void OnGet()
        {
            var redirectUrl = string.Format("{0}://{1}{2}", Request.Scheme, Request.Host, Url.Content("~/api/journal/authenticate"));
            var _randomState = _memoryCache.GetOrCreate("frontierLogin-" + HttpContext.Connection.RemoteIpAddress.ToString(), (entry) =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20);
                return "jl-" + DateTime.Now.Ticks;
            });

            Response.Redirect($"https://auth.frontierstore.net/auth?state={_randomState}&response_type=code&scope=auth%20capi&approval_prompt=auto&redirect_uri={redirectUrl}&client_id={_configuration["EliteDangerous:ClientId"]}");
        }
    }
}
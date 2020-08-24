using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using System;

namespace Journal_Limpet.Pages
{
    public class LoginModel : PageModel
    {
        private readonly IConfiguration _configuration;

        public LoginModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        public void OnGet()
        {
            var redirectUrl = string.Format("{0}://{1}{2}", Request.Scheme, Request.Host, Url.Content("~/api/journal/authenticate"));
            var _randomState = "jl-" + DateTime.Now.Ticks;
            Response.Redirect($"https://auth.frontierstore.net/auth?state={_randomState}&response_type=code&approval_prompt=auto&redirect_uri={redirectUrl}&client_id={_configuration["EliteDangerous:ClientId"]}");
        }
    }
}
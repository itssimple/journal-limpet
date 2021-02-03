using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Journal_Limpet.Pages
{
    [Authorize]
    public class LiveAccountLogModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}

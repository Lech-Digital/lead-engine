using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages;

// The profile page grew into /app/settings; old links and hints keep working.
public sealed class ProfileRedirectModel : PageModel
{
    public IActionResult OnGet() => RedirectPermanent("/app/settings/company");
}

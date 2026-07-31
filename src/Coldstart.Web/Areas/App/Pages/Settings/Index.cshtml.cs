using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages.Settings;

[Authorize]
public sealed class SettingsIndexModel : PageModel
{
    public IActionResult OnGet() => Redirect("/app/settings/company");
}

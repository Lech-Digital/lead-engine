using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.Identity.Pages.Account;

// Public self-registration is disabled — Coldstart is invite-only. Both verbs bounce to Login.
public sealed class RegisterModel : PageModel
{
    public IActionResult OnGet() => Redirect("/Identity/Account/Login");
    public IActionResult OnPost() => Redirect("/Identity/Account/Login");
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.Identity.Pages.Account;

// Public self-registration is disabled — Coldstart is invite-only. Both verbs bounce to Login.
// IgnoreAntiforgeryToken so a token-less POST still reaches OnPost and returns a deterministic
// 302 redirect, instead of Razor Pages' automatic antiforgery validation short-circuiting with 400.
[IgnoreAntiforgeryToken]
public sealed class RegisterModel : PageModel
{
    public IActionResult OnGet() => Redirect("/Identity/Account/Login");
    public IActionResult OnPost() => Redirect("/Identity/Account/Login");
}

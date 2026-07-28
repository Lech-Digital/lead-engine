using System.ComponentModel.DataAnnotations;
using Coldstart.Infrastructure.Identity;
using Coldstart.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.Identity.Pages.Account;

public sealed class LoginModel(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> users) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public string? ReturnUrl { get; private set; }

    public IEnumerable<AuthenticationScheme> ExternalLogins { get; private set; } = [];

    public async Task OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        ExternalLogins = await signInManager.GetExternalAuthenticationSchemesAsync();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        ExternalLogins = await signInManager.GetExternalAuthenticationSchemesAsync();

        if (!ModelState.IsValid)
            return Page();

        var result = await signInManager.PasswordSignInAsync(
            Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            ErrorMessage = "This account is temporarily locked after too many failed attempts. " +
                           "Wait 15 minutes and try again, or reset your password.";
            return Page();
        }
        if (!result.Succeeded)
        {
            ErrorMessage = "Invalid login attempt.";
            return Page();
        }

        if (returnUrl is not null) return LocalRedirect(returnUrl);
        var user = await users.FindByEmailAsync(Input.Email);
        return LocalRedirect(await RoleRedirect.PathForAsync(users, user!, CancellationToken.None));
    }

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        public bool RememberMe { get; set; }
    }
}

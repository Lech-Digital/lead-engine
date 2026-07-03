using System.ComponentModel.DataAnnotations;
using Coldstart.Application.Customers;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.Identity.Pages.Account;

public sealed class RegisterModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IProvisionCustomer provision) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<string> Errors { get; private set; } = [];

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

        var user = new ApplicationUser { UserName = Input.Email, Email = Input.Email };
        var result = await userManager.CreateAsync(user, Input.Password);

        if (!result.Succeeded)
        {
            Errors = result.Errors.Select(e => e.Description).ToList();
            return Page();
        }

        if (user.CustomerId is null)
        {
            user.CustomerId = await provision.ProvisionAsync(user.Email!, CancellationToken.None);
            await userManager.UpdateAsync(user);
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return LocalRedirect(returnUrl ?? "/app");
    }

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }
}

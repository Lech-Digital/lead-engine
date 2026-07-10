using System.Security.Claims;
using Coldstart.Application.Customers;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.Identity.Pages.Account;

public sealed class ExternalLoginModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IProvisionCustomer provision) : PageModel
{
    public string? ErrorMessage { get; private set; }

    // Entry point: Login/Register post here per provider to start the challenge.
    public IActionResult OnPost(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback", values: new { returnUrl });
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Challenge(properties, provider);
    }

    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl ??= "/app";

        if (remoteError is not null)
        {
            ErrorMessage = $"Error from external provider: {remoteError}";
            return RedirectToPage("./Login");
        }

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            ErrorMessage = "Error loading external login information.";
            return RedirectToPage("./Login");
        }

        // Existing external login already linked — sign in directly.
        var signInResult = await signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
        if (signInResult.Succeeded)
            return LocalRedirect(returnUrl);

        if (signInResult.IsLockedOut)
        {
            ErrorMessage = "This account is blocked. Contact your administrator.";
            return RedirectToPage("./Login");
        }

        var emailClaim = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrEmpty(emailClaim))
        {
            var existing = await userManager.FindByEmailAsync(emailClaim);
            if (existing is not null)
            {
                if (!await signInManager.CanSignInAsync(existing))
                {
                    ErrorMessage = "This account is blocked. Contact your administrator.";
                    return RedirectToPage("./Login");
                }

                // Merge: link the external login to the pre-existing local account.
                await userManager.AddLoginAsync(existing, info);
                await signInManager.SignInAsync(existing, isPersistent: false);
                return LocalRedirect(returnUrl);
            }
        }

        if (string.IsNullOrEmpty(emailClaim))
        {
            ErrorMessage = "External provider did not supply an email address.";
            return RedirectToPage("./Login");
        }

        // No existing account — create, link, and provision a new one.
        var user = new ApplicationUser { UserName = emailClaim, Email = emailClaim, EmailConfirmed = true };
        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            ErrorMessage = string.Join(" ", createResult.Errors.Select(e => e.Description));
            return RedirectToPage("./Login");
        }

        var addLoginResult = await userManager.AddLoginAsync(user, info);
        if (!addLoginResult.Succeeded)
        {
            ErrorMessage = string.Join(" ", addLoginResult.Errors.Select(e => e.Description));
            return RedirectToPage("./Login");
        }

        user.CustomerId = await provision.ProvisionAsync(user.Email!, HttpContext.RequestAborted);
        await userManager.UpdateAsync(user);

        if (!await signInManager.CanSignInAsync(user))
        {
            ErrorMessage = "This account is blocked. Contact your administrator.";
            return RedirectToPage("./Login");
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return LocalRedirect(returnUrl);
    }
}

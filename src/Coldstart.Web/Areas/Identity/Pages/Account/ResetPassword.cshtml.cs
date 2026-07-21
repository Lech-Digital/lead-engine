using System.ComponentModel.DataAnnotations;
using System.Text;
using Coldstart.Application.Ports;
using Coldstart.Infrastructure.Identity;
using Coldstart.Web.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace Coldstart.Web.Areas.Identity.Pages.Account;

public sealed class ResetPasswordModel(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IEmailSender email,
    ILogger<ResetPasswordModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public IActionResult OnGet(string? code = null, string? email = null)
    {
        if (string.IsNullOrEmpty(code))
        {
            // Render (rather than 400) so the page still serves its antiforgery-bearing form;
            // a user landing here without a code just sees "invalid link" instead of a raw error.
            Errors = ["A password-set code is required. Use the link from your email."];
            return Page();
        }
        Input = new InputModel { Code = code, Email = email ?? "" };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return Page();

        var user = await users.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            Errors = ["This reset link is no longer valid. Request a new one."];
            return Page();
        }

        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Input.Code));
        var result = await users.ResetPasswordAsync(user, token, Input.Password);
        if (!result.Succeeded)
        {
            Errors = result.Errors.Select(e => e.Description).ToList();
            return Page();
        }

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
            // Invite completion — first successful password set. Welcome them; best-effort,
            // a mail failure must not block the sign-in below.
            try
            {
                var appUrl = $"{Request.Scheme}://{Request.Host}/app";
                var welcome = EmailTemplates.Welcome(appUrl);
                await email.SendAsync(user.Email!, welcome.Subject, welcome.TextBody, welcome.HtmlBody, ct);
            }
            catch (Exception ex) { logger.LogWarning(ex, "Welcome email to {Email} failed; invite completion continues.", user.Email); }
        }
        await signIn.SignInAsync(user, isPersistent: false);
        return LocalRedirect(await RoleRedirect.PathForAsync(users, user, ct));
    }

    public sealed class InputModel
    {
        [Required] public string Email { get; set; } = "";
        [Required] public string Code { get; set; } = "";
        [Required, DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6)]
        public string Password { get; set; } = "";
        [Required, DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }
}

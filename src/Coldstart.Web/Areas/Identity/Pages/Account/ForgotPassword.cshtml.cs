using System.ComponentModel.DataAnnotations;
using System.Text;
using Coldstart.Application.Ports;
using Coldstart.Infrastructure.Identity;
using Coldstart.Web.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Coldstart.Web.Areas.Identity.Pages.Account;

public sealed class ForgotPasswordModel(
    UserManager<ApplicationUser> users,
    IEmailSender email,
    ILogger<ForgotPasswordModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool Sent { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return Page();

        var user = await users.FindByEmailAsync(Input.Email);
        if (user is not null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var link = Url.Page("/Account/ResetPassword", pageHandler: null,
                values: new { area = "Identity", code, email = Input.Email }, protocol: Request.Scheme)!;
            var content = EmailTemplates.PasswordReset(link);
            try
            {
                await email.SendAsync(Input.Email, content.Subject, content.TextBody, content.HtmlBody, ct);
            }
            catch (Exception ex)
            {
                // The page must stay neutral (no account enumeration) and never 500 on a
                // mail outage — but a reset email that silently vanishes must be loggable.
                logger.LogError(ex, "Password-reset email to {Email} failed to send.", Input.Email);
            }
        }

        Sent = true;   // neutral result regardless of whether the account exists
        return Page();
    }

    public sealed class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";
    }
}

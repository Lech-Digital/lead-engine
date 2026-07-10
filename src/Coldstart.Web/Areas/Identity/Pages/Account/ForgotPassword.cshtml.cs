using System.ComponentModel.DataAnnotations;
using System.Text;
using Coldstart.Application.Ports;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Coldstart.Web.Areas.Identity.Pages.Account;

public sealed class ForgotPasswordModel(UserManager<ApplicationUser> users, IEmailSender email) : PageModel
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
            await email.SendAsync(Input.Email, "Reset your Coldstart password",
                $"Set a new password for your Coldstart account:\n\n{link}\n\nThis link expires in 7 days. If you didn't ask for this, ignore this email.",
                null, ct);
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

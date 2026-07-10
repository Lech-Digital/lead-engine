using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Coldstart.Application.Customers;
using Coldstart.Application.Ports;
using Coldstart.Infrastructure.Identity;
using Coldstart.Web.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Coldstart.Web.Areas.Admin.Pages;

[Authorize(Roles = ColdstartRoles.SuperAdmin)]
public sealed class IndexModel(
    UserManager<ApplicationUser> users,
    IProvisionCustomer provision,
    IEmailSender email) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? StatusMessage { get; private set; }
    public IReadOnlyList<Row> Pending { get; private set; } = [];
    public IReadOnlyList<Row> Accepted { get; private set; } = [];

    public sealed record Row(string Email, int? DaysLeft);

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }

        if (await users.FindByEmailAsync(Input.Email) is not null)
        {
            ModelState.AddModelError("Input.Email", "That person already has an account.");
            await LoadAsync();
            return Page();
        }

        var user = new ApplicationUser
        {
            UserName = Input.Email,
            Email = Input.Email,
            EmailConfirmed = false,
            InvitedAt = DateTimeOffset.UtcNow,
        };
        var created = await users.CreateAsync(user, RandomPassword());
        if (!created.Succeeded)
        {
            foreach (var e in created.Errors) ModelState.AddModelError("", e.Description);
            await LoadAsync();
            return Page();
        }

        await users.AddToRoleAsync(user, ColdstartRoles.CompanyAdmin);
        user.CustomerId = await provision.ProvisionAsync(Input.Company, ct);
        await users.UpdateAsync(user);

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = Url.Page("/Account/ResetPassword", pageHandler: null,
            values: new { area = "Identity", code, email = Input.Email }, protocol: Request.Scheme)!;
        var content = EmailTemplates.Invitation(Input.Company, link);
        await email.SendAsync(Input.Email, content.Subject, content.TextBody, content.HtmlBody, ct);

        StatusMessage = $"Invitation sent to {Input.Email}.";
        Input = new InputModel();
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        var admins = await users.GetUsersInRoleAsync(ColdstartRoles.CompanyAdmin);
        Pending = admins.Where(u => !u.EmailConfirmed)
            .Select(u => new Row(u.Email!, DaysLeft(u.InvitedAt))).ToList();
        Accepted = admins.Where(u => u.EmailConfirmed)
            .Select(u => new Row(u.Email!, null)).ToList();
    }

    private static int? DaysLeft(DateTimeOffset? invitedAt) =>
        invitedAt is { } t ? Math.Max(0, 7 - (int)(DateTimeOffset.UtcNow - t).TotalDays) : null;

    private static string RandomPassword() =>
        "Aa1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    public sealed class InputModel
    {
        [Required] public string Company { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
    }
}

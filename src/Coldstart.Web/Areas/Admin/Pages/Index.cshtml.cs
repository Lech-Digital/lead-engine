using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Coldstart.Application.Customers;
using Coldstart.Application.Ports;
using Coldstart.Web.Email;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Web.Areas.Admin.Pages;

[Authorize(Roles = ColdstartRoles.SuperAdmin)]
public sealed class IndexModel(
    UserManager<ApplicationUser> users,
    IProvisionCustomer provision,
    ICustomerRepository customers,
    IDeleteCustomer deleteCustomer,
    IEmailSender email) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? StatusMessage { get; private set; }
    public IReadOnlyList<UserRow> Rows { get; private set; } = [];
    public (string Email, string Url)? CopyLink { get; private set; }

    public sealed record UserRow(string Id, string Email, string Company, string Role, string Status, int? DaysLeft, bool CanManage);

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostInviteAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }

        if (await users.FindByEmailAsync(Input.Email) is not null)
        {
            ModelState.AddModelError("Input.Email", "That person already has an account.");
            await LoadAsync(ct);
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
            await LoadAsync(ct);
            return Page();
        }

        await users.AddToRoleAsync(user, ColdstartRoles.CompanyAdmin);
        user.CustomerId = await provision.ProvisionAsync(Input.Company, ct);
        await users.UpdateAsync(user);

        await SendInviteAsync(user, Input.Company, ct);

        StatusMessage = $"Invitation sent to {Input.Email}.";
        Input = new InputModel();
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostResendAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) { StatusMessage = "No account specified."; await LoadAsync(ct); return Page(); }

        var user = await users.FindByEmailAsync(email);
        if (user is { EmailConfirmed: false } && await IsCompanyAdminAsync(user))
        {
            var company = await CompanyNameAsync(user, ct);
            await SendInviteAsync(user, company, ct);
            StatusMessage = $"Invitation re-sent to {email}.";
        }
        else StatusMessage = "Nothing to resend for that account.";
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) { StatusMessage = "No account specified."; await LoadAsync(ct); return Page(); }

        var user = await users.FindByEmailAsync(email);
        if (user is { EmailConfirmed: false } && await IsCompanyAdminAsync(user))
        {
            var customerId = user.CustomerId;
            var result = await users.DeleteAsync(user);
            if (!result.Succeeded)
            {
                StatusMessage = $"Could not remove {email}.";
            }
            else
            {
                if (customerId is { } cid) await deleteCustomer.DeleteAsync(cid, ct);
                StatusMessage = $"Invitation to {email} revoked.";
            }
        }
        else StatusMessage = "Only pending invites can be revoked.";
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCopyLinkAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) { StatusMessage = "No account specified."; await LoadAsync(ct); return Page(); }

        var user = await users.FindByEmailAsync(email);
        if (user is { EmailConfirmed: false } && await IsCompanyAdminAsync(user))
            CopyLink = (email, await BuildSetPasswordLinkAsync(user));
        await LoadAsync(ct);
        return Page();
    }

    private async Task<bool> IsCompanyAdminAsync(ApplicationUser user) =>
        await users.IsInRoleAsync(user, ColdstartRoles.CompanyAdmin);

    private async Task<string> CompanyNameAsync(ApplicationUser user, CancellationToken ct)
    {
        if (user.CustomerId is not { } cid) return "your company";
        var all = await customers.AllAsync(ct);
        return all.FirstOrDefault(c => c.Id.Value == cid)?.Name ?? "your company";
    }

    private async Task SendInviteAsync(ApplicationUser user, string company, CancellationToken ct)
    {
        var link = await BuildSetPasswordLinkAsync(user);
        var content = EmailTemplates.Invitation(company, link);
        await email.SendAsync(user.Email!, content.Subject, content.TextBody, content.HtmlBody, ct);
    }

    private async Task<string> BuildSetPasswordLinkAsync(ApplicationUser user)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        return Url.Page("/Account/ResetPassword", pageHandler: null,
            values: new { area = "Identity", code, email = user.Email }, protocol: Request.Scheme)!;
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var all = await customers.AllAsync(ct);
        var names = all.ToDictionary(c => c.Id.Value, c => c.Name);
        var currentEmail = User.Identity?.Name;

        var rows = new List<UserRow>();
        foreach (var u in await users.Users.ToListAsync(ct))
        {
            var roles = await users.GetRolesAsync(u);
            var role = roles.FirstOrDefault() ?? "";
            var isSuper = role == ColdstartRoles.SuperAdmin;
            var status = !u.EmailConfirmed ? "Pending"
                : u.LockoutEnd is { } le && le > DateTimeOffset.UtcNow ? "Blocked" : "Active";
            var company = u.CustomerId is { } cid && names.TryGetValue(cid, out var n) ? n : "—";
            rows.Add(new UserRow(u.Id, u.Email!, company, role, status,
                DaysLeft(u.InvitedAt), CanManage: !isSuper && !string.Equals(u.Email, currentEmail, StringComparison.OrdinalIgnoreCase)));
        }
        Rows = rows.OrderBy(r => r.Company).ThenBy(r => r.Email).ToList();
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

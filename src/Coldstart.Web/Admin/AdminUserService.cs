using System.Security.Cryptography;
using System.Text;
using Coldstart.Application.Billing;
using Coldstart.Application.Customers;
using Coldstart.Application.Harvest;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure.Identity;
using Coldstart.Infrastructure.Notifications;
using Coldstart.Web.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coldstart.Web.Admin;

internal enum AdminOutcome { Ok, NotFound, Forbidden, Conflict }

public sealed record AdminUserRow(
    string Id, string Email, string Company, string Role, string Status,
    int? DaysLeft, bool CanManage, Guid? CustomerId,
    CustomerPlan? Plan = null, bool HasSubscription = false, DateTimeOffset? ComplimentaryUntil = null,
    HarvestStatus? HarvestStatus = null, DateTimeOffset? LastHarvestAt = null);

internal sealed record AdminActionResult(
    AdminOutcome Outcome, string Message, string? SetPasswordLink = null, Guid? CustomerId = null)
{
    public bool Succeeded => Outcome == AdminOutcome.Ok;
}

internal sealed record InviteRequest(string Email, string Company, string Niche, string? Info);

// SuperAdmin user administration, shared by the /admin Razor page and /api/admin.
// Web layer on purpose: it is ASP.NET-Identity-bound and must not sink into Application.
// actorEmail = who is acting (null for API-key calls): the self-target guard applies only
// when known; SuperAdmin targets are always refused.
internal sealed class AdminUserService(
    UserManager<ApplicationUser> users,
    IProvisionCustomer provision,
    ICustomerRepository customers,
    IDeleteCustomer deleteCustomer,
    IEmailSender email,
    ISearchProfileGenerator profileGen,
    IHarvestQueue harvestQueue,
    IGrantComplimentaryAccess grantComplimentary,
    ICancelComplimentaryAccess cancelComplimentary,
    IOptions<NotificationsOptions> notifications,
    LinkGenerator links,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<IReadOnlyList<AdminUserRow>> ListAsync(string? actorEmail, CancellationToken ct)
    {
        var all = await customers.AllAsync(ct);
        var byId = all.ToDictionary(c => c.Id.Value, c => c);

        var rows = new List<AdminUserRow>();
        foreach (var u in await users.Users.ToListAsync(ct))
        {
            var roles = await users.GetRolesAsync(u);
            var role = roles.FirstOrDefault() ?? "";
            var isSuper = role == ColdstartRoles.SuperAdmin;
            var status = !u.EmailConfirmed ? "Pending"
                : u.LockoutEnd is { } le && le > DateTimeOffset.UtcNow ? "Blocked" : "Active";
            var customer = u.CustomerId is { } cid && byId.TryGetValue(cid, out var c) ? c : null;
            var company = customer?.Name ?? "—";
            rows.Add(new AdminUserRow(u.Id, u.Email!, company, role, status,
                DaysLeft(u.InvitedAt),
                CanManage: !isSuper && !string.Equals(u.Email, actorEmail, StringComparison.OrdinalIgnoreCase),
                u.CustomerId,
                customer?.Plan, customer?.StripeSubscriptionId is not null, customer?.ComplimentaryUntil,
                customer?.HarvestStatus, customer?.LastHarvestAt));
        }
        return rows.OrderBy(r => r.Company).ThenBy(r => r.Email).ToList();
    }

    // Grants (or extends — GrantComplimentary always sets a NEW end date from now, it never adds
    // to whatever remained) complimentary full-plan access, then emails the customer's confirmed
    // address, mirroring what the /api/admin/customers/{id}/complimentary endpoint does.
    public async Task<AdminActionResult> GrantComplimentaryAsync(Guid customerId, int days, CancellationToken ct)
    {
        var result = await grantComplimentary.ExecuteAsync(customerId, days, ct);
        if (!result.Succeeded)
            return new(result.Message.Contains("No such customer") ? AdminOutcome.NotFound : AdminOutcome.Conflict, result.Message);

        var to = await users.Users
            .Where(u => u.CustomerId == customerId && u.Email != null && u.EmailConfirmed)
            .OrderBy(u => u.Email).Select(u => u.Email!).FirstOrDefaultAsync(ct);
        if (to is not null)
        {
            var appUrl = $"{notifications.Value.BaseUrl.TrimEnd('/')}/app";
            var content = EmailTemplates.ComplimentaryGranted(result.CustomerName!, result.Until!.Value, appUrl);
            try { await email.SendAsync(to, content.Subject, content.TextBody, content.HtmlBody, ct); }
            catch { /* the grant stands even if mail fails */ }
        }
        return new(AdminOutcome.Ok, result.Message);
    }

    // Revokes an active complimentary grant immediately. Never emails — only a grant notifies.
    public async Task<AdminActionResult> CancelComplimentaryAsync(Guid customerId, CancellationToken ct)
    {
        var result = await cancelComplimentary.ExecuteAsync(customerId, ct);
        return result.Succeeded
            ? new(AdminOutcome.Ok, result.Message)
            : new(result.Message.Contains("No such customer") ? AdminOutcome.NotFound : AdminOutcome.Conflict, result.Message);
    }

    // Enqueues a harvest, same mechanism as POST /api/admin/harvest/{customerId}. Refuses a
    // second run while one is already Pending or Running so the operator can't pile up jobs.
    public async Task<AdminActionResult> TriggerHarvestAsync(Guid customerId, CancellationToken ct)
    {
        var customer = (await customers.AllAsync(ct)).FirstOrDefault(c => c.Id.Value == customerId);
        if (customer is null)
            return new(AdminOutcome.NotFound, "No such customer.");
        if (customer.HarvestStatus is HarvestStatus.Pending or HarvestStatus.Running)
            return new(AdminOutcome.Conflict,
                $"A harvest for {customer.Name} is already {customer.HarvestStatus.ToString().ToLowerInvariant()} — wait for it to finish before queuing another.");

        harvestQueue.Enqueue(new HarvestJob(customerId));
        return new(AdminOutcome.Ok, $"Harvest queued for {customer.Name} — it runs in the background; check back shortly.");
    }

    public async Task<AdminActionResult> InviteAsync(InviteRequest request, CancellationToken ct)
    {
        if (await users.FindByEmailAsync(request.Email) is not null)
            return new(AdminOutcome.Conflict, "That person already has an account.");

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = false,
            InvitedAt = DateTimeOffset.UtcNow,
        };
        var created = await users.CreateAsync(user, RandomPassword());
        if (!created.Succeeded)
            return new(AdminOutcome.Conflict, string.Join(" ", created.Errors.Select(e => e.Description)));

        await users.AddToRoleAsync(user, ColdstartRoles.CompanyAdmin);

        SearchProfile? profile = null;
        try { profile = await profileGen.GenerateAsync(new SearchProfileRequest(request.Niche, request.Info ?? ""), ct); }
        catch { /* generation must never block the invite */ }

        user.CustomerId = await provision.ProvisionAsync(
            request.Company, new CompanyProfileSeed(request.Niche, request.Info, profile), ct);
        await users.UpdateAsync(user);

        var link = await SendInviteAsync(user, request.Company, ct);
        harvestQueue.Enqueue(new HarvestJob(user.CustomerId.Value));

        return new(AdminOutcome.Ok, $"Invitation sent to {request.Email}.", link, user.CustomerId);
    }

    public async Task<AdminActionResult> ResendAsync(string email, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null || user.EmailConfirmed || !await IsCompanyAdminAsync(user))
            return new(AdminOutcome.NotFound, "Nothing to resend for that account.");

        var company = await CompanyNameAsync(user, ct);
        var link = await SendInviteAsync(user, company, ct);
        return new(AdminOutcome.Ok, $"Invitation re-sent to {email}.", link);
    }

    public async Task<AdminActionResult> RevokeAsync(string email, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null || user.EmailConfirmed || !await IsCompanyAdminAsync(user))
            return new(AdminOutcome.Conflict, "Only pending invites can be revoked.");

        var customerId = user.CustomerId;
        var result = await users.DeleteAsync(user);
        if (!result.Succeeded)
            return new(AdminOutcome.Conflict, $"Could not remove {email}.");

        if (customerId is { } cid) await deleteCustomer.DeleteAsync(cid, ct);
        return new(AdminOutcome.Ok, $"Invitation to {email} revoked.");
    }

    public async Task<AdminActionResult> CopyLinkAsync(string email, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null || user.EmailConfirmed || !await IsCompanyAdminAsync(user))
            return new(AdminOutcome.NotFound, "Only pending invites have a set-password link.");

        return new(AdminOutcome.Ok, $"Set-password link for {email}.", await BuildSetPasswordLinkAsync(user));
    }

    public async Task<AdminActionResult> BlockAsync(string email, string? actorEmail, CancellationToken ct)
    {
        var (user, error) = await GuardedAsync(email, actorEmail);
        if (user is null) return error!;

        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await users.UpdateSecurityStampAsync(user);   // invalidate any live cookie
        return new(AdminOutcome.Ok, $"{user.Email} is blocked.");
    }

    public async Task<AdminActionResult> UnblockAsync(string email, string? actorEmail, CancellationToken ct)
    {
        var (user, error) = await GuardedAsync(email, actorEmail);
        if (user is null) return error!;

        await users.SetLockoutEndDateAsync(user, null);
        return new(AdminOutcome.Ok, $"{user.Email} is active again.");
    }

    public async Task<AdminActionResult> RemoveAsync(string email, string? actorEmail, CancellationToken ct)
    {
        var (user, error) = await GuardedAsync(email, actorEmail);
        if (user is null) return error!;

        var customerId = user.CustomerId;
        var result = await users.DeleteAsync(user);
        if (!result.Succeeded)
            return new(AdminOutcome.Conflict, "Could not remove that account.");

        if (customerId is { } cid) await deleteCustomer.DeleteAsync(cid, ct);
        return new(AdminOutcome.Ok, "Account removed.");
    }

    // Returns the target only if it is safe to act on: exists, is not a SuperAdmin,
    // and is not the actor themselves.
    private async Task<(ApplicationUser? User, AdminActionResult? Error)> GuardedAsync(string email, string? actorEmail)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
            return (null, new AdminActionResult(AdminOutcome.NotFound, "No such account."));
        if (await users.IsInRoleAsync(user, ColdstartRoles.SuperAdmin)
            || string.Equals(user.Email, actorEmail, StringComparison.OrdinalIgnoreCase))
            return (null, new AdminActionResult(AdminOutcome.Forbidden, "That account can't be modified."));
        return (user, null);
    }

    private async Task<bool> IsCompanyAdminAsync(ApplicationUser user) =>
        await users.IsInRoleAsync(user, ColdstartRoles.CompanyAdmin);

    private async Task<string> CompanyNameAsync(ApplicationUser user, CancellationToken ct)
    {
        if (user.CustomerId is not { } cid) return "your company";
        var all = await customers.AllAsync(ct);
        return all.FirstOrDefault(c => c.Id.Value == cid)?.Name ?? "your company";
    }

    private async Task<string> SendInviteAsync(ApplicationUser user, string company, CancellationToken ct)
    {
        var link = await BuildSetPasswordLinkAsync(user);
        var content = EmailTemplates.Invitation(company, link);
        await email.SendAsync(user.Email!, content.Subject, content.TextBody, content.HtmlBody, ct);
        return link;
    }

    private async Task<string> BuildSetPasswordLinkAsync(ApplicationUser user)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var ctx = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No active HTTP request to build the link from.");
        return links.GetUriByPage(ctx, "/Account/ResetPassword", handler: null,
                values: new { area = "Identity", code, email = user.Email })
            ?? throw new InvalidOperationException("Could not build the set-password link.");
    }

    private static int? DaysLeft(DateTimeOffset? invitedAt) =>
        invitedAt is { } t ? Math.Max(0, 7 - (int)(DateTimeOffset.UtcNow - t).TotalDays) : null;

    private static string RandomPassword() =>
        "Aa1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
}

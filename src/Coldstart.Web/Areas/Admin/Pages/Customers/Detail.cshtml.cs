using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure.Identity;
using Coldstart.Web.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Web.Areas.Admin.Pages.Customers;

// The operator's one place to manage a single customer: who has access, what plan they're on,
// their harvest engine, and their lead counts. Everything that used to be crammed into one row
// of the /admin table lives here instead, scoped to this customer.
[Authorize(Roles = ColdstartRoles.SuperAdmin)]
internal sealed class DetailModel(
    AdminUserService admin,
    UserManager<ApplicationUser> users,
    ICustomerRepository customers) : PageModel
{
    public Guid CustomerId { get; private set; }
    public string Company { get; private set; } = "";
    public string? Niche { get; private set; }
    public DateTimeOffset? CreatedAt { get; private set; }

    public CustomerPlan Plan { get; private set; }
    public bool HasSubscription { get; private set; }
    public DateTimeOffset? PlanRenewsAt { get; private set; }
    public DateTimeOffset? ComplimentaryUntil { get; private set; }

    public HarvestStatus HarvestStatus { get; private set; }
    public DateTimeOffset? LastHarvestAt { get; private set; }

    public int LeadsTotal { get; private set; }
    public int LeadsPendingReview { get; private set; }

    public IReadOnlyList<AdminUserRow> People { get; private set; } = [];

    [BindProperty] public int GrantDays { get; set; } = 5;
    public string? StatusMessage { get; private set; }
    public (string Email, string Url)? CopyLink { get; private set; }

    // Carries a status message across the redirect that follows a Revoke/Remove that deleted
    // this customer entirely — there's no detail page left to re-render, so we land on the
    // companies list instead. See RenderOrRedirectAsync.
    [TempData] public string? Flash { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct) =>
        await LoadAsync(id, ct) ? Page() : NotFound();

    public async Task<IActionResult> OnPostResendAsync(Guid id, string email, CancellationToken ct)
    {
        StatusMessage = (await admin.ResendAsync(email, ct)).Message;
        return await RenderAsync(id, ct);
    }

    // Revoking a pending invite deletes that user AND its tenant — if this was the customer's
    // only account (the normal case: an invite that was never accepted), the customer itself is
    // now gone, so we can't re-render this page. RenderOrRedirectAsync falls back to /admin.
    public async Task<IActionResult> OnPostRevokeAsync(Guid id, string email, CancellationToken ct)
    {
        var message = (await admin.RevokeAsync(email, ct)).Message;
        return await RenderOrRedirectAsync(id, message, ct);
    }

    public async Task<IActionResult> OnPostCopyLinkAsync(Guid id, string email, CancellationToken ct)
    {
        var result = await admin.CopyLinkAsync(email, ct);
        if (result.Succeeded) CopyLink = (email, result.SetPasswordLink!);
        else StatusMessage = result.Message;
        return await RenderAsync(id, ct);
    }

    public async Task<IActionResult> OnPostBlockAsync(Guid id, string userId, CancellationToken ct)
    {
        StatusMessage = await ActByIdAsync(userId, email => admin.BlockAsync(email, User.Identity?.Name, ct));
        return await RenderAsync(id, ct);
    }

    public async Task<IActionResult> OnPostUnblockAsync(Guid id, string userId, CancellationToken ct)
    {
        StatusMessage = await ActByIdAsync(userId, email => admin.UnblockAsync(email, User.Identity?.Name, ct));
        return await RenderAsync(id, ct);
    }

    // Same tenant-deletion concern as Revoke: removing this customer's last (or only) account
    // deletes the tenant too, so fall back to /admin rather than re-rendering a gone customer.
    public async Task<IActionResult> OnPostRemoveAsync(Guid id, string userId, CancellationToken ct)
    {
        var message = await ActByIdAsync(userId, email => admin.RemoveAsync(email, User.Identity?.Name, ct));
        return await RenderOrRedirectAsync(id, message, ct);
    }

    public async Task<IActionResult> OnPostGrantComplimentaryAsync(Guid id, CancellationToken ct)
    {
        StatusMessage = (await admin.GrantComplimentaryAsync(id, GrantDays, ct)).Message;
        return await RenderAsync(id, ct);
    }

    public async Task<IActionResult> OnPostCancelComplimentaryAsync(Guid id, CancellationToken ct)
    {
        StatusMessage = (await admin.CancelComplimentaryAsync(id, ct)).Message;
        return await RenderAsync(id, ct);
    }

    public async Task<IActionResult> OnPostRunHarvestAsync(Guid id, CancellationToken ct)
    {
        StatusMessage = (await admin.TriggerHarvestAsync(id, ct)).Message;
        return await RenderAsync(id, ct);
    }

    // The row buttons post the Identity user id; the service is keyed by email. Deliberately not
    // scope-checked against the route's customer id — AdminUserService's own guard (no touching a
    // SuperAdmin or yourself) is the actual authority here, same as it was on the old /admin page.
    private async Task<string> ActByIdAsync(string userId, Func<string, Task<AdminActionResult>> action)
    {
        var user = await users.FindByIdAsync(userId);
        if (user?.Email is not { } email) return "No such account.";
        return (await action(email)).Message;
    }

    // See IndexModel.RenderAsync for why ModelState/BindProperty state must be cleared before
    // re-rendering: the row-action forms only post a bare field, and Razor Pages' empty-prefix
    // binding fallback would otherwise leave spurious validation errors on the page.
    private async Task<IActionResult> RenderAsync(Guid id, CancellationToken ct)
    {
        ModelState.Clear();
        return await LoadAsync(id, ct) ? Page() : NotFound();
    }

    // Used after Revoke/Remove, which can delete the customer outright: if it's still there,
    // re-render this page as usual (e.g. the action was refused by a guard); if not, the detail
    // page has nothing left to show, so hand the message to /admin via TempData and redirect.
    private async Task<IActionResult> RenderOrRedirectAsync(Guid id, string message, CancellationToken ct)
    {
        ModelState.Clear();
        if (await LoadAsync(id, ct))
        {
            StatusMessage = message;
            return Page();
        }
        Flash = message;
        return RedirectToPage("/Index", new { area = "Admin" });
    }

    private async Task<bool> LoadAsync(Guid id, CancellationToken ct)
    {
        var customer = await customers.GetForSystemAsync(new CustomerId(id), ct);
        if (customer is null) return false;

        CustomerId = id;
        Company = customer.Name;
        Niche = string.IsNullOrWhiteSpace(customer.Profile.Niche) ? null : customer.Profile.Niche;
        Plan = customer.Plan;
        HasSubscription = customer.StripeSubscriptionId is not null;
        PlanRenewsAt = customer.PlanRenewsAt;
        ComplimentaryUntil = customer.ComplimentaryUntil;
        HarvestStatus = customer.HarvestStatus;
        LastHarvestAt = customer.LastHarvestAt;
        GrantDays = 5;

        var counts = await customers.LeadCountsAsync(ct);
        counts.TryGetValue(id, out var lc);
        LeadsTotal = lc?.Total ?? 0;
        LeadsPendingReview = lc?.PendingReview ?? 0;

        var allRows = await admin.ListAsync(User.Identity?.Name, ct);
        People = allRows.Where(r => r.CustomerId == id).OrderBy(r => r.Email).ToList();

        // The tenant's creation moment isn't tracked on Customer itself, but provisioning happens
        // at invite time — so the earliest invited-at among this customer's users is the same instant.
        var invitedDates = (await users.Users.Where(u => u.CustomerId == id).Select(u => u.InvitedAt).ToListAsync(ct))
            .Where(d => d.HasValue).Select(d => d!.Value).ToList();
        CreatedAt = invitedDates.Count > 0 ? invitedDates.Min() : null;

        return true;
    }
}

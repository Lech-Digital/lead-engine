using Coldstart.Application.Customers;
using Coldstart.Application.Hints;
using Coldstart.Application.Leads;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages;

[Authorize]
public sealed class IndexModel(UserManager<ApplicationUser> users, IGetLeadInbox inbox, IGetCompanyProfile profiles, ILeadWorkflow workflow) : PageModel
{
    public string CustomerName { get; private set; } = "—";
    public CompanyProfileView Profile { get; private set; } = new(null, null, null, null, null, null, null);

    public IReadOnlyList<LeadSummary> Queue { get; private set; } = [];
    public int OlderUnreviewedCount { get; private set; }
    public int OvernightCount { get; private set; }
    public int SentTodayCount { get; private set; }
    public int HistoryCount { get; private set; }
    public DateTimeOffset? LatestFound { get; private set; }

    public Hint? ActiveHint { get; private set; }

    public Coldstart.Domain.CustomerPlan Plan { get; private set; }
    public Coldstart.Domain.HarvestStatus HarvestStatus { get; private set; }
    public int LockedCount { get; private set; }
    public int TotalLeads { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return;

        // Resolve today's onboarding hint (only for a signed-in user with a provisioned customer).
        var decision = HintPicker.Pick(ReadHintState(user), DateOnly.FromDateTime(DateTime.UtcNow));
        ActiveHint = decision.ToShow;
        if (decision.NextState.ShownOn != user.HintShownOn)   // a hint was promoted today → persist the stamp
        {
            user.HintShownOn = decision.NextState.ShownOn;
            await users.UpdateAsync(user);
        }

        var result = await inbox.ForCustomerAsync(user.CustomerId.Value, ct);
        CustomerName = result.CustomerName;
        Plan = result.Plan;
        HarvestStatus = result.HarvestStatus;
        LockedCount = result.LockedCount;
        TotalLeads = result.Leads.Count;
        Profile = await profiles.ForCustomerAsync(user.CustomerId.Value, ct);

        var unfinished = result.Leads
            .Where(l => l.Status is "PendingReview" or "Approved")
            .OrderByDescending(l => l.CreatedAt).ToList();
        Queue = unfinished.Take(LeadBriefing.QueueCap).ToList();
        OlderUnreviewedCount = Math.Max(0, unfinished.Count - LeadBriefing.QueueCap);

        var todayUtc = DateTime.UtcNow.Date;
        OvernightCount = unfinished.Count(l => l.CreatedAt.UtcDateTime.Date == todayUtc);
        LatestFound = unfinished.Count > 0 ? unfinished.Max(l => l.CreatedAt) : null;
        SentTodayCount = result.Leads.Count(l => l.Status == "Sent" && l.CreatedAt.UtcDateTime.Date == todayUtc);
        HistoryCount = result.Leads.Count(l => l.Status is "Sent" or "Shelved" or "Rejected") + OlderUnreviewedCount;
    }

    public async Task<IActionResult> OnPostSkipAsync(Guid id, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });
        await workflow.DiscardAsync(user.CustomerId.Value, id, ct);
        return RedirectToPage("/Index", new { area = "App" });
    }

    public async Task<IActionResult> OnPostDismissHintAsync(string key, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is not null && OnboardingHints.All.Any(h => h.Key == key) && !ReadHintState(user).SeenKeys.Contains(key))
        {
            var next = HintPicker.Dismiss(ReadHintState(user), key, DateOnly.FromDateTime(DateTime.UtcNow));
            user.HintsSeenCsv = WriteSeen(next.SeenKeys);
            user.HintShownOn = next.ShownOn;
            user.HintDismissedOn = next.DismissedOn;
            await users.UpdateAsync(user);
        }
        return RedirectToPage("/Index", new { area = "App" });
    }

    private static HintState ReadHintState(ApplicationUser user) => new(
        SeenKeys: string.IsNullOrWhiteSpace(user.HintsSeenCsv)
            ? []
            : user.HintsSeenCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        ShownOn: user.HintShownOn,
        DismissedOn: user.HintDismissedOn);

    private static string? WriteSeen(IReadOnlyCollection<string> keys) =>
        keys.Count == 0 ? null : string.Join(',', keys);
}

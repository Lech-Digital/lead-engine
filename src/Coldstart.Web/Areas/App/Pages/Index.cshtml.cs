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

    public string Tab { get; private set; } = "new";
    public IReadOnlyList<LeadSummary> New { get; private set; } = [];
    public IReadOnlyList<LeadSummary> AcceptedReady { get; private set; } = [];
    public IReadOnlyList<LeadSummary> Sent { get; private set; } = [];
    public IReadOnlyList<LeadSummary> Shelved { get; private set; } = [];
    public IReadOnlyList<LeadSummary> AutoSkipped { get; private set; } = [];

    public Hint? ActiveHint { get; private set; }

    public int NewCount => New.Count;
    public int AcceptedCount => AcceptedReady.Count + Sent.Count;
    public int DiscardedCount => Shelved.Count + AutoSkipped.Count;

    public async Task OnGetAsync(string? tab, CancellationToken ct)
    {
        Tab = tab is "accepted" or "discarded" ? tab : "new";

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
        Profile = await profiles.ForCustomerAsync(user.CustomerId.Value, ct);

        New = result.Leads.Where(l => l.Status == "PendingReview").ToList();
        AcceptedReady = result.Leads.Where(l => l.Status == "Approved").ToList();
        Sent = result.Leads.Where(l => l.Status == "Sent").ToList();
        Shelved = result.Leads.Where(l => l.Status == "Shelved").ToList();
        AutoSkipped = result.Leads.Where(l => l.Status == "Rejected").ToList();
    }

    public async Task<IActionResult> OnPostMoveAsync(Guid id, string tab, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return new JsonResult(new { ok = false, message = "Not signed in." });
        var cid = user.CustomerId.Value;

        var result = tab switch
        {
            "new" => await workflow.ReopenAsync(cid, id, ct),
            "accepted" => await workflow.ApproveAsync(cid, id, ct),
            "discarded" => await workflow.DiscardAsync(cid, id, ct),
            _ => LeadActionResult.Invalid("Unknown tab."),
        };
        return new JsonResult(new { ok = result.IsOk, status = result.Status.ToString(), message = result.Message });
    }

    public async Task<IActionResult> OnPostDismissHintAsync(string key, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is not null && !string.IsNullOrWhiteSpace(key))
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

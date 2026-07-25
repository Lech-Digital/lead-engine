using Coldstart.Application.Leads;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages;

[Authorize]
public sealed class HistoryModel(UserManager<ApplicationUser> users, IGetLeadInbox inbox, ILeadWorkflow workflow) : PageModel
{
    public string CustomerName { get; private set; } = "—";
    public string Filter { get; private set; } = "all";
    public string? Query { get; private set; }
    public IReadOnlyList<LeadSummary> Rows { get; private set; } = [];
    public int AllCount { get; private set; }
    public int SentCount { get; private set; }
    public int SkippedCount { get; private set; }
    public int OlderCount { get; private set; }

    public async Task OnGetAsync(string? filter, string? q, CancellationToken ct)
    {
        Filter = filter is "sent" or "skipped" or "older" ? filter : "all";
        Query = q;
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return;
        var result = await inbox.ForCustomerAsync(user.CustomerId.Value, ct);
        CustomerName = result.CustomerName;

        var unfinished = result.Leads.Where(l => l.Status is "PendingReview" or "Approved")
            .OrderByDescending(l => l.CreatedAt).ToList();
        var older = unfinished.Skip(LeadBriefing.QueueCap).ToList();          // beyond the queue cap
        var sent = result.Leads.Where(l => l.Status == "Sent").ToList();
        var skipped = result.Leads.Where(l => l.Status is "Shelved" or "Rejected").ToList();

        SentCount = sent.Count; SkippedCount = skipped.Count; OlderCount = older.Count;
        AllCount = sent.Count + skipped.Count + older.Count;

        IEnumerable<LeadSummary> rows = Filter switch
        {
            "sent" => sent, "skipped" => skipped, "older" => older,
            _ => sent.Concat(skipped).Concat(older),
        };
        if (!string.IsNullOrWhiteSpace(q))
            rows = rows.Where(l => l.Company.Contains(q, StringComparison.OrdinalIgnoreCase));
        Rows = rows.OrderByDescending(l => l.CreatedAt).ToList();
    }

    public async Task<IActionResult> OnPostReopenAsync(Guid id, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is not null) await workflow.ReopenAsync(user.CustomerId.Value, id, ct);
        return RedirectToPage("/History", new { area = "App" });
    }
}

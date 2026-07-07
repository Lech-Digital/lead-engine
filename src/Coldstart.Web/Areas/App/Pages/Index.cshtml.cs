using Coldstart.Application.Customers;
using Coldstart.Application.Leads;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages;

[Authorize]
public sealed class IndexModel(UserManager<ApplicationUser> users, IGetLeadInbox inbox, IGetCompanyProfile profiles) : PageModel
{
    public string CustomerName { get; private set; } = "—";
    public CompanyProfileView Profile { get; private set; } = new(null, null, null, null, null, null, null);

    public string Tab { get; private set; } = "new";
    public IReadOnlyList<LeadSummary> New { get; private set; } = [];
    public IReadOnlyList<LeadSummary> AcceptedReady { get; private set; } = [];
    public IReadOnlyList<LeadSummary> Sent { get; private set; } = [];
    public IReadOnlyList<LeadSummary> Shelved { get; private set; } = [];
    public IReadOnlyList<LeadSummary> AutoSkipped { get; private set; } = [];

    public int NewCount => New.Count;
    public int AcceptedCount => AcceptedReady.Count + Sent.Count;
    public int DiscardedCount => Shelved.Count + AutoSkipped.Count;

    public async Task OnGetAsync(string? tab, CancellationToken ct)
    {
        Tab = tab is "accepted" or "discarded" ? tab : "new";

        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return;

        var result = await inbox.ForCustomerAsync(user.CustomerId.Value, ct);
        CustomerName = result.CustomerName;
        Profile = await profiles.ForCustomerAsync(user.CustomerId.Value, ct);

        New = result.Leads.Where(l => l.Status == "PendingReview").ToList();
        AcceptedReady = result.Leads.Where(l => l.Status == "Approved").ToList();
        Sent = result.Leads.Where(l => l.Status == "Sent").ToList();
        Shelved = result.Leads.Where(l => l.Status == "Shelved").ToList();
        AutoSkipped = result.Leads.Where(l => l.Status == "Rejected").ToList();
    }
}

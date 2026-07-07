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
    public IReadOnlyList<LeadSummary> Leads { get; private set; } = [];
    public CompanyProfileView Profile { get; private set; } = new(null, null, null, null, null, null, null);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return;
        var result = await inbox.ForCustomerAsync(user.CustomerId.Value, ct);
        CustomerName = result.CustomerName;
        Leads = result.Leads;
        Profile = await profiles.ForCustomerAsync(user.CustomerId.Value, ct);
    }
}

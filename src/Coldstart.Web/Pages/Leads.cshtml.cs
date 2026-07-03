using Coldstart.Application.Leads;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Pages;

public sealed class LeadsModel(IGetLeadInbox inbox) : PageModel
{
    public string CustomerName { get; private set; } = "—";
    public IReadOnlyList<LeadSummary> Leads { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var result = await inbox.ForFirstCustomerAsync(ct);
        CustomerName = result.CustomerName;
        Leads = result.Leads;
    }
}

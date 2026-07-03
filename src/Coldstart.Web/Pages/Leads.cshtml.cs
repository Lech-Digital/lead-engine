using Coldstart.Application.Leads;
using Coldstart.Application.Ports;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Pages;

public sealed class LeadsModel(ICustomerRepository customers, IListLeads listLeads) : PageModel
{
    public string CustomerName { get; private set; } = "—";
    public IReadOnlyList<LeadSummary> Leads { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var all = await customers.AllAsync(ct);
        var customer = all.FirstOrDefault();
        if (customer is null) return;
        CustomerName = customer.Name;
        Leads = await listLeads.ForCustomerAsync(customer.Id, ct);
    }
}

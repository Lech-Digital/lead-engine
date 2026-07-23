using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

internal sealed class GetLeadInbox(ICustomerRepository customers, IListLeads listLeads) : IGetLeadInbox
{
    public async Task<LeadInbox> ForCustomerAsync(Guid customerId, CancellationToken ct)
    {
        var id = new CustomerId(customerId);
        // Load only this tenant's own row (goes through the tenant query filter) — not every
        // customer via the admin bypass.
        var customer = await customers.GetAsync(id, ct);
        if (customer is null)
            return new LeadInbox("—", Array.Empty<LeadSummary>());

        var leads = await listLeads.ForCustomerAsync(id, ct);
        return new LeadInbox(customer.Name, leads);
    }
}

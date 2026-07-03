using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

internal sealed class GetLeadInbox(ICustomerRepository customers, IListLeads listLeads) : IGetLeadInbox
{
    public async Task<LeadInbox> ForCustomerAsync(Guid customerId, CancellationToken ct)
    {
        var id = new CustomerId(customerId);
        var all = await customers.AllAsync(ct);
        var customer = all.FirstOrDefault(c => c.Id == id);
        if (customer is null)
            return new LeadInbox("—", Array.Empty<LeadSummary>());

        var leads = await listLeads.ForCustomerAsync(id, ct);
        return new LeadInbox(customer.Name, leads);
    }
}

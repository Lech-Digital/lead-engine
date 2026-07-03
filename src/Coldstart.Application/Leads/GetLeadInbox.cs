using Coldstart.Application.Ports;

namespace Coldstart.Application.Leads;

internal sealed class GetLeadInbox(ICustomerRepository customers, IListLeads listLeads) : IGetLeadInbox
{
    public async Task<LeadInbox> ForFirstCustomerAsync(CancellationToken ct)
    {
        var all = await customers.AllAsync(ct);
        var customer = all.FirstOrDefault();
        if (customer is null)
            return new LeadInbox("—", Array.Empty<LeadSummary>());

        var leads = await listLeads.ForCustomerAsync(customer.Id, ct);
        return new LeadInbox(customer.Name, leads);
    }
}

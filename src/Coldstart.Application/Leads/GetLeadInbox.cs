using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

internal sealed class GetLeadInbox(ICustomerRepository customers, IListLeads listLeads, QuotaOptions quota) : IGetLeadInbox
{
    public async Task<LeadInbox> ForCustomerAsync(Guid customerId, CancellationToken ct)
    {
        var id = new CustomerId(customerId);
        var customer = await customers.GetAsync(id, ct);
        if (customer is null)
            return new LeadInbox("—", Array.Empty<LeadSummary>(), CustomerPlan.Free, 0, HarvestStatus.None);

        var leads = await listLeads.ForCustomerAsync(id, customer.Plan, quota.FreeLeadCount, ct);
        var lockedCount = leads.Count(l => l.Locked);
        return new LeadInbox(customer.Name, leads, customer.Plan, lockedCount, customer.HarvestStatus);
    }
}

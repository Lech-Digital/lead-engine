using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

internal sealed class ListLeads(ILeadRepository leads) : IListLeads
{
    public async Task<IReadOnlyList<LeadSummary>> ForCustomerAsync(CustomerId customerId, CustomerPlan plan, int freeCount, CancellationToken ct)
    {
        var found = await leads.ForCustomerAsync(customerId, ct);
        var locked = LeadLocking.LockedLeadIds(found, plan, freeCount);
        return found
            .Select(l => new LeadSummary(
                l.Id.Value, l.Company, l.Fit.ToString(), l.Status.ToString(),
                l.RecipientEmail?.Value, l.EmailConfidence.ToString(), l.Subject, l.Draft,
                l.Source, l.Score, l.CreatedAt, l.MatchedSignals,
                Locked: locked.Contains(l.Id.Value)))
            .ToList();
    }
}

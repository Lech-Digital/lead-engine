using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

internal sealed class ListLeads(ILeadRepository leads) : IListLeads
{
    public async Task<IReadOnlyList<LeadSummary>> ForCustomerAsync(CustomerId customerId, CancellationToken ct)
    {
        var found = await leads.ForCustomerAsync(customerId, ct);
        return found
            // The inbox is the review queue — qualified-out (Rejected) and set-aside
            // (Shelved) leads are persisted for dedup but never shown here.
            .Where(l => l.Status is not (LeadStatus.Rejected or LeadStatus.Shelved))
            .Select(l => new LeadSummary(
                l.Id.Value,
                l.Company,
                l.Fit.ToString(),
                l.Status.ToString(),
                l.RecipientEmail?.Value,
                l.EmailConfidence.ToString(),
                l.Subject,
                l.Draft,
                l.Source,
                l.Score))
            .ToList();
    }
}

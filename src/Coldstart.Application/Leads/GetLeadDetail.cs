using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

internal sealed class GetLeadDetail(ILeadRepository leads, ICustomerRepository customers, QuotaOptions quota) : IGetLeadDetail
{
    public async Task<LeadDetail?> ForLeadAsync(Guid customerId, Guid leadId, CancellationToken ct)
    {
        var lead = await leads.GetAsync(new LeadId(leadId), ct);
        if (lead is null || lead.CustomerId.Value != customerId) return null;

        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is not null)
        {
            var all = await leads.ForCustomerAsync(new CustomerId(customerId), ct);
            if (LeadLocking.LockedLeadIds(all, customer.Plan, quota.FreeLeadCount).Contains(leadId))
                return null;   // locked behind the free-tier foil — not accessible
        }

        return new LeadDetail(
            lead.Id.Value, lead.Company, lead.Fit.ToString(), lead.Status.ToString(),
            lead.Subject, lead.Draft, lead.RecipientEmail?.Value,
            lead.Source, lead.SourceUrl, lead.Score, lead.SentAt,
            ContactLinkBuilder.Build(lead.Company, lead.SourceUrl),
            lead.MatchedSignals,
            lead.EmailConfidence.ToString(),
            lead.Notes.OrderByDescending(n => n.CreatedAt).Select(n => new LeadNoteView(n.Id, n.Text, n.CreatedAt, n.UpdatedAt)).ToList(),
            lead.Outcome.ToString());
    }
}

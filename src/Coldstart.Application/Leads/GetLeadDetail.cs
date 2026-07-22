using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

internal sealed class GetLeadDetail(ILeadRepository leads) : IGetLeadDetail
{
    public async Task<LeadDetail?> ForLeadAsync(Guid customerId, Guid leadId, CancellationToken ct)
    {
        var lead = await leads.GetAsync(new LeadId(leadId), ct);
        if (lead is null || lead.CustomerId.Value != customerId) return null;

        return new LeadDetail(
            lead.Id.Value, lead.Company, lead.Fit.ToString(), lead.Status.ToString(),
            lead.Subject, lead.Draft, lead.RecipientEmail?.Value,
            lead.Source, lead.SourceUrl, lead.Score, lead.SentAt,
            ContactLinkBuilder.Build(lead.Company, lead.SourceUrl),
            lead.MatchedSignals,
            lead.EmailConfidence.ToString(),
            lead.Notes.Select(n => new LeadNoteView(n.Id, n.Text, n.CreatedAt, n.UpdatedAt)).ToList());
    }
}

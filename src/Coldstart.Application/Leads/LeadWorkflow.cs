using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

internal sealed class LeadWorkflow(ILeadRepository leads) : ILeadWorkflow
{
    public Task<LeadActionResult> SaveDraftAsync(Guid customerId, Guid leadId, string subject, string draft, string? recipient, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead =>
        {
            lead.ReviseDraft(subject, draft);
            return ApplyRecipient(lead, recipient);
        });

    public Task<LeadActionResult> AcceptAsync(Guid customerId, Guid leadId, string subject, string draft, string? recipient, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead =>
        {
            lead.ReviseDraft(subject, draft);
            var r = ApplyRecipient(lead, recipient);
            if (!r.IsOk) return r;
            lead.Approve();
            return LeadActionResult.Ok;
        });

    public Task<LeadActionResult> DiscardAsync(Guid customerId, Guid leadId, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead => { lead.Discard(); return LeadActionResult.Ok; });

    public Task<LeadActionResult> ReopenAsync(Guid customerId, Guid leadId, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead => { lead.Reopen(); return LeadActionResult.Ok; });

    public Task<LeadActionResult> MarkSentAsync(Guid customerId, Guid leadId, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead =>
        {
            if (lead.RecipientEmail is null)
                return LeadActionResult.Invalid("A recipient is required before marking as sent.");
            lead.MarkSent(DateTimeOffset.UtcNow);
            return LeadActionResult.Ok;
        });

    // Empty/whitespace recipient is allowed (optional); a non-empty invalid one is rejected.
    private static LeadActionResult ApplyRecipient(Lead lead, string? recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient)) return LeadActionResult.Ok;
        EmailAddress email;
        try { email = new EmailAddress(recipient); }
        catch (DomainException ex) { return LeadActionResult.Invalid(ex.Message); }
        lead.SetRecipient(email);
        return LeadActionResult.Ok;
    }

    // Single tenant-isolation choke point: load, verify ownership, mutate, persist.
    private async Task<LeadActionResult> MutateAsync(Guid customerId, Guid leadId, CancellationToken ct, Func<Lead, LeadActionResult> mutate)
    {
        var lead = await leads.GetAsync(new LeadId(leadId), ct);
        if (lead is null || lead.CustomerId.Value != customerId)
            return LeadActionResult.NotFound;

        LeadActionResult result;
        try { result = mutate(lead); }
        catch (DomainException ex) { return LeadActionResult.Invalid(ex.Message); }
        if (!result.IsOk) return result;

        await leads.UpdateAsync(lead, ct);
        return LeadActionResult.Ok;
    }
}

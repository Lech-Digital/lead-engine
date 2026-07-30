using Coldstart.Application.Outreach;
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

internal sealed class LeadWorkflow(ILeadRepository leads, IOutreachMailer outreach, ICustomerRepository customers, QuotaOptions quota) : ILeadWorkflow
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

    public Task<LeadActionResult> ApproveAsync(Guid customerId, Guid leadId, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead => { lead.Approve(); return LeadActionResult.Ok; });

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

    public async Task<LeadActionResult> SendFromColdstartAsync(Guid customerId, Guid leadId, CancellationToken ct)
    {
        var lead = await leads.GetAsync(new LeadId(leadId), ct);
        if (lead is null || lead.CustomerId.Value != customerId)
            return LeadActionResult.NotFound;
        if (await IsLockedAsync(customerId, leadId, ct))
            return LeadActionResult.NotFound;
        if (lead.Status != LeadStatus.Approved)
            return LeadActionResult.Invalid("Approve the lead before sending from Coldstart.");
        if (lead.RecipientEmail is null)
            return LeadActionResult.Invalid("A recipient is required before sending.");

        var sender = await customers.GetAsync(new CustomerId(customerId), ct);
        try
        {
            await outreach.SendAsync(new OutreachEmail(lead.RecipientEmail.Value, lead.Subject, lead.Draft,
                sender?.Profile.CtaButtonText), ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return LeadActionResult.Invalid("Couldn't send just now — the email wasn't sent. Please try again.");
        }

        lead.MarkSent(DateTimeOffset.UtcNow);
        await leads.UpdateAsync(lead, ct);
        return LeadActionResult.Ok;
    }

    public Task<LeadActionResult> AddNoteAsync(Guid customerId, Guid leadId, Guid noteId, string text, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead => { lead.AddNote(noteId, text, DateTimeOffset.UtcNow); return LeadActionResult.Ok; });

    public Task<LeadActionResult> UpdateNoteAsync(Guid customerId, Guid leadId, Guid noteId, string text, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead => { lead.UpdateNote(noteId, text, DateTimeOffset.UtcNow); return LeadActionResult.Ok; });

    public Task<LeadActionResult> RemoveNoteAsync(Guid customerId, Guid leadId, Guid noteId, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead => { lead.RemoveNote(noteId); return LeadActionResult.Ok; });

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

    // Locked leads (beyond the free-tier quota) are not actionable. Runs after the ownership check.
    private async Task<bool> IsLockedAsync(Guid customerId, Guid leadId, CancellationToken ct)
    {
        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is null) return false;
        var all = await leads.ForCustomerAsync(new CustomerId(customerId), ct);
        return Leads.LeadLocking.LockedLeadIds(all, customer.Plan, quota.FreeLeadCount).Contains(leadId);
    }

    // Single tenant-isolation choke point: load, verify ownership, mutate, persist.
    private async Task<LeadActionResult> MutateAsync(Guid customerId, Guid leadId, CancellationToken ct, Func<Lead, LeadActionResult> mutate)
    {
        var lead = await leads.GetAsync(new LeadId(leadId), ct);
        if (lead is null || lead.CustomerId.Value != customerId)
            return LeadActionResult.NotFound;
        if (await IsLockedAsync(customerId, leadId, ct))
            return LeadActionResult.NotFound;

        LeadActionResult result;
        try { result = mutate(lead); }
        catch (DomainException ex) { return LeadActionResult.Invalid(ex.Message); }
        if (!result.IsOk) return result;

        await leads.UpdateAsync(lead, ct);
        return LeadActionResult.Ok;
    }
}

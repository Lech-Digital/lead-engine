namespace Coldstart.Application.Leads;

public interface ILeadWorkflow
{
    Task<LeadActionResult> SaveDraftAsync(Guid customerId, Guid leadId, string subject, string draft, string? recipient, CancellationToken ct);
    Task<LeadActionResult> AcceptAsync(Guid customerId, Guid leadId, string subject, string draft, string? recipient, CancellationToken ct);
    Task<LeadActionResult> ApproveAsync(Guid customerId, Guid leadId, CancellationToken ct);
    Task<LeadActionResult> DiscardAsync(Guid customerId, Guid leadId, CancellationToken ct, string? reason = null);
    Task<LeadActionResult> RecordOutcomeAsync(Guid customerId, Guid leadId, Coldstart.Domain.LeadOutcome outcome, CancellationToken ct);
    Task<LeadActionResult> ReopenAsync(Guid customerId, Guid leadId, CancellationToken ct);
    Task<LeadActionResult> MarkSentAsync(Guid customerId, Guid leadId, CancellationToken ct);
    Task<LeadActionResult> SendFromColdstartAsync(Guid customerId, Guid leadId, CancellationToken ct);
    Task<LeadActionResult> AddNoteAsync(Guid customerId, Guid leadId, Guid noteId, string text, CancellationToken ct);
    Task<LeadActionResult> UpdateNoteAsync(Guid customerId, Guid leadId, Guid noteId, string text, CancellationToken ct);
    Task<LeadActionResult> RemoveNoteAsync(Guid customerId, Guid leadId, Guid noteId, CancellationToken ct);
}

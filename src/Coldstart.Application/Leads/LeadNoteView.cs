namespace Coldstart.Application.Leads;

public sealed record LeadNoteView(Guid Id, string Text, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

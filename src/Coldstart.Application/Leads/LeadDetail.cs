namespace Coldstart.Application.Leads;

public sealed record LeadDetail(
    Guid Id,
    string Company,
    string Fit,
    string Status,
    string Subject,
    string Draft,
    string? RecipientEmail,
    string Source,
    string SourceUrl,
    double Score,
    DateTimeOffset? SentAt,
    IReadOnlyList<ContactLink> ContactLinks,
    string MatchedSignals,
    string EmailConfidence,
    IReadOnlyList<LeadNoteView> Notes);

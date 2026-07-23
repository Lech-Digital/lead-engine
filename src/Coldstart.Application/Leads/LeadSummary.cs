namespace Coldstart.Application.Leads;

public sealed record LeadSummary(
    Guid Id,
    string Company,
    string Fit,
    string Status,
    string? RecipientEmail,
    string EmailConfidence,
    string Subject,
    string Draft,
    string Source,
    double Score,
    DateTimeOffset CreatedAt,
    string MatchedSignals,
    bool Locked = false);

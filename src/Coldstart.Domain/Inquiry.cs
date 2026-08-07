namespace Coldstart.Domain;

public sealed class Inquiry
{
    public InquiryId Id { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public EmailAddress Email { get; private set; } = null!;
    public string Niche { get; private set; } = string.Empty;
    public string WhatYouSell { get; private set; } = string.Empty;
    public string? Company { get; private set; }
    public string? Message { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    // When the person ticked the processing-consent box (GDPR audit trail); null on legacy rows.
    public DateTimeOffset? ConsentGivenAt { get; private set; }
    // Operator marked this as spam/not-a-fit; null = not dismissed. This is real state (unlike
    // "converted", which is derived from whether a user account exists for the email) because
    // spam-or-not is a judgement call nothing else can reconstruct. Dismissing never deletes the
    // row — it carries a GDPR consent trail — so Restore can always bring it back with no loss.
    public DateTimeOffset? DismissedAt { get; private set; }

    private Inquiry() { } // EF

    private Inquiry(InquiryId id, string name, EmailAddress email, string niche,
        string whatYouSell, string? message, DateTimeOffset createdAt, string? company, DateTimeOffset? consentGivenAt)
    {
        Id = id;
        Name = name;
        Company = company;
        Email = email;
        Niche = niche;
        WhatYouSell = whatYouSell;
        Message = message;
        CreatedAt = createdAt;
        ConsentGivenAt = consentGivenAt;
    }

    public static Inquiry Create(string name, EmailAddress email, string niche,
        string whatYouSell, string? message, DateTimeOffset createdAt, string? company = null,
        DateTimeOffset? consentGivenAt = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Inquiry name is required.");
        if (email is null) throw new DomainException("Inquiry email is required.");
        return new Inquiry(InquiryId.New(), name.Trim(), email, niche?.Trim() ?? "",
            whatYouSell?.Trim() ?? "", string.IsNullOrWhiteSpace(message) ? null : message.Trim(), createdAt,
            string.IsNullOrWhiteSpace(company) ? null : company.Trim(), consentGivenAt);
    }

    // Idempotent by design: the admin UI never re-offers Dismiss on an already-dismissed row (it
    // offers Restore instead), so a repeat call can only be a double-submit or a race between two
    // tabs — refreshing the timestamp instead of throwing keeps that a harmless no-op rather than
    // an error the operator can't do anything about.
    public void Dismiss(DateTimeOffset when) => DismissedAt = when;

    // Also idempotent: restoring a row that isn't dismissed just confirms it's already open.
    public void Restore() => DismissedAt = null;
}

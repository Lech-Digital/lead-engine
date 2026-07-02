namespace Coldstart.Domain;

public sealed class Lead
{
    public LeadId Id { get; private set; } = null!;
    public CustomerId CustomerId { get; private set; } = null!;
    public string Company { get; private set; } = string.Empty;
    public Fit Fit { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string Draft { get; private set; } = string.Empty;
    public LeadStatus Status { get; private set; }
    public EmailAddress? RecipientEmail { get; private set; }
    public EmailConfidence EmailConfidence { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    private Lead() { } // EF materialization

    private Lead(LeadId id, CustomerId customerId, string company, Fit fit,
        string subject, string draft, DateTimeOffset createdAt)
    {
        Id = id;
        CustomerId = customerId;
        Company = company;
        Fit = fit;
        Subject = subject;
        Draft = draft;
        CreatedAt = createdAt;
        Status = LeadStatus.PendingReview;
        EmailConfidence = EmailConfidence.None;
    }

    public static Lead Create(CustomerId customerId, string company, Fit fit,
        string subject, string draft, DateTimeOffset createdAt) =>
        new(LeadId.New(), customerId, company, fit, subject, draft, createdAt);

    public void Approve(EmailAddress recipient)
    {
        if (Status == LeadStatus.Sent)
            throw new DomainException("Cannot approve an already-sent lead.");
        RecipientEmail = recipient ?? throw new DomainException("Approval requires a recipient email.");
        Status = LeadStatus.Approved;
    }

    public void Reject() => Status = LeadStatus.Rejected;

    public void Shelve() => Status = LeadStatus.Shelved;

    public void MarkSent(DateTimeOffset when)
    {
        if (Status != LeadStatus.Approved || RecipientEmail is null)
            throw new DomainException("Only an approved lead with a recipient can be marked sent.");
        Status = LeadStatus.Sent;
        SentAt = when;
    }
}

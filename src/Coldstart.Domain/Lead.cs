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
    public string Source { get; private set; } = "";
    public string SourceUrl { get; private set; } = "";
    public string Fingerprint { get; private set; } = "";
    public double Score { get; private set; }
    public string MatchedSignals { get; private set; } = "";

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

    public static Lead CreateHarvested(CustomerId customerId, string company, Fit fit,
        string subject, string draft, string source, string sourceUrl, string fingerprint,
        double score, string matchedSignals, DateTimeOffset createdAt)
    {
        var lead = new Lead(LeadId.New(), customerId, company, fit, subject, draft, createdAt)
        {
            Source = source,
            SourceUrl = sourceUrl,
            Fingerprint = fingerprint,
            Score = score,
            MatchedSignals = matchedSignals,
        };
        return lead;
    }

    public void Approve(EmailAddress? recipient = null)
    {
        if (Status == LeadStatus.Sent)
            throw new DomainException("Cannot change a lead that has already been sent.");
        if (recipient is not null) RecipientEmail = recipient;
        Status = LeadStatus.Approved;
    }

    public void SetRecipient(EmailAddress recipient)
    {
        if (Status == LeadStatus.Sent)
            throw new DomainException("Cannot change a lead that has already been sent.");
        RecipientEmail = recipient ?? throw new DomainException("Recipient is required.");
    }

    public void ReviseDraft(string subject, string draft)
    {
        if (Status == LeadStatus.Sent)
            throw new DomainException("Cannot edit a lead that has already been sent.");
        Subject = subject ?? "";
        Draft = draft ?? "";
    }

    public void Discard()
    {
        if (Status == LeadStatus.Sent)
            throw new DomainException("Cannot discard a lead that has already been sent.");
        Status = LeadStatus.Shelved;
    }

    public void Reopen()
    {
        if (Status == LeadStatus.Sent)
            throw new DomainException("Cannot reopen a lead that has already been sent.");
        Status = LeadStatus.PendingReview;
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

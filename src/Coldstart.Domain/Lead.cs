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
    // Post-send outcome (customer-reported) and why a skipped lead was skipped (feeds the enricher).
    public LeadOutcome Outcome { get; private set; }
    public DateTimeOffset? OutcomeAt { get; private set; }
    public string? SkipReason { get; private set; }

    private readonly List<LeadNote> _notes = [];
    public IReadOnlyList<LeadNote> Notes => _notes;

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

    public void Discard(string? reason = null)
    {
        if (Status == LeadStatus.Sent)
            throw new DomainException("Cannot discard a lead that has already been sent.");
        Status = LeadStatus.Shelved;
        SkipReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public void Reopen()
    {
        if (Status == LeadStatus.Sent)
            throw new DomainException("Cannot reopen a lead that has already been sent.");
        Status = LeadStatus.PendingReview;
        SkipReason = null;
    }

    public void RecordOutcome(LeadOutcome outcome, DateTimeOffset when)
    {
        if (Status != LeadStatus.Sent)
            throw new DomainException("An outcome can only be recorded on a sent lead.");
        Outcome = outcome;
        OutcomeAt = outcome == LeadOutcome.None ? null : when;
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

    public Guid AddNote(string text, DateTimeOffset when) => AddNote(Guid.NewGuid(), text, when);

    public Guid AddNote(Guid id, string text, DateTimeOffset when)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException("Note text is required.");
        _notes.Add(new LeadNote(id, text.Trim(), when));
        return id;
    }

    public void UpdateNote(Guid noteId, string text, DateTimeOffset when)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException("Note text is required.");
        _notes.FirstOrDefault(n => n.Id == noteId)?.Update(text.Trim(), when);
    }

    public void RemoveNote(Guid noteId) => _notes.RemoveAll(n => n.Id == noteId);
}

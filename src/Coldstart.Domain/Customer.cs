namespace Coldstart.Domain;

public sealed class Customer
{
    private readonly List<Lead> _leads = new();

    public CustomerId Id { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public CompanyProfile Profile { get; private set; } = CompanyProfile.Empty;
    public Targeting Targeting { get; private set; } = Targeting.Empty;
    public CustomerPlan Plan { get; private set; } = CustomerPlan.Free;
    public HarvestStatus HarvestStatus { get; private set; } = HarvestStatus.None;
    public DateTimeOffset? LastHarvestAt { get; private set; }
    public string? StripeCustomerId { get; private set; }
    public string? StripeSubscriptionId { get; private set; }
    public DateTimeOffset? PlanRenewsAt { get; private set; }
    // Complimentary (non-Stripe) full access, e.g. tester thank-yous. Null = none.
    // While set, Plan is Full; the nightly sweep downgrades once it lapses.
    public DateTimeOffset? ComplimentaryUntil { get; private set; }
    // Briefing push channels (customer-configured; null = channel off).
    public string? SlackWebhookUrl { get; private set; }
    public string? WhatsAppNumber { get; private set; }
    // Comma-separated source names the customer opted into; null = all sources.
    public string? EnabledSourcesCsv { get; private set; }
    // Trial follow-up nudges (Free plan): highest stage already sent (0 = none), and opt-out.
    public int FollowUpStage { get; private set; }
    public bool FollowUpsOptOut { get; private set; }
    // Retention clock (privacy policy §7): when a paid subscription ended, and whether the
    // operator has been reminded that the 60-day deletion window is closing.
    public DateTimeOffset? SubscriptionEndedAt { get; private set; }
    public bool RetentionReminderSent { get; private set; }
    public IReadOnlyList<Lead> Leads => _leads.AsReadOnly();

    private Customer() { } // EF materialization

    private Customer(CustomerId id, string name)
    {
        Id = id;
        Name = name;
    }

    public static Customer Create(string name) => new(CustomerId.New(), name);

    public void UpdateProfile(CompanyProfile profile) => Profile = profile ?? CompanyProfile.Empty;

    public void UpdateTargeting(Targeting targeting) => Targeting = targeting ?? Targeting.Empty;

    public void SetPlan(CustomerPlan plan) => Plan = plan;

    public void MarkHarvest(HarvestStatus status, DateTimeOffset? at)
    {
        HarvestStatus = status;
        if (at is not null) LastHarvestAt = at;
    }

    public Lead AddLead(string company, Fit fit, string subject, string draft, DateTimeOffset createdAt)
    {
        var lead = Lead.Create(Id, company, fit, subject, draft, createdAt);
        _leads.Add(lead);
        return lead;
    }

    // Monotonic: a lower or repeated stage never rewinds progress (guards double-sends).
    public void MarkFollowUpSent(int stage)
    {
        if (stage > FollowUpStage) FollowUpStage = stage;
    }

    public void OptOutOfFollowUps() => FollowUpsOptOut = true;

    public void LinkStripeCustomer(string stripeCustomerId) => StripeCustomerId = stripeCustomerId;

    public void ActivateFull(string subscriptionId, DateTimeOffset? renewsAt)
    {
        Plan = CustomerPlan.Full;
        StripeSubscriptionId = subscriptionId;
        PlanRenewsAt = renewsAt;
        SubscriptionEndedAt = null;         // re-subscribing stops the retention clock
        RetentionReminderSent = false;
        ComplimentaryUntil = null;          // a real subscription supersedes the trial
    }

    public void GrantComplimentary(DateTimeOffset until, DateTimeOffset now)
    {
        if (StripeSubscriptionId is not null)
            throw new DomainException("This customer already has a paid subscription — nothing to grant.");
        if (until <= now)
            throw new DomainException("The complimentary end date must be in the future.");
        Plan = CustomerPlan.Full;
        ComplimentaryUntil = until;
        SubscriptionEndedAt = null;         // active access stops the retention clock, as ActivateFull does
        RetentionReminderSent = false;
    }

    // Revokes an active complimentary grant immediately, returning the customer to Free.
    // A comp trial never paid, so — unlike DowngradeToFree(endedAt) after a real subscription —
    // this must NOT touch SubscriptionEndedAt/RetentionReminderSent; there is no privacy-policy
    // §7 retention clock to start for access that was never paid for.
    public void CancelComplimentary()
    {
        if (ComplimentaryUntil is null)
            throw new DomainException("This customer has no active complimentary grant to cancel.");
        Plan = CustomerPlan.Free;
        ComplimentaryUntil = null;
    }

    public void MarkRetentionReminderSent() => RetentionReminderSent = true;

    public void ConfigureSources(IReadOnlyCollection<string>? sourceNames) =>
        EnabledSourcesCsv = sourceNames is null || sourceNames.Count == 0
            ? null
            : string.Join(',', sourceNames.Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct());

    public bool AllowsSource(string sourceName) =>
        EnabledSourcesCsv is null
        || EnabledSourcesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(sourceName, StringComparer.OrdinalIgnoreCase);

    public void ConfigureNotifications(string? slackWebhookUrl, string? whatsAppNumber)
    {
        SlackWebhookUrl = string.IsNullOrWhiteSpace(slackWebhookUrl) ? null : slackWebhookUrl.Trim();
        WhatsAppNumber = string.IsNullOrWhiteSpace(whatsAppNumber) ? null : whatsAppNumber.Trim();
    }

    public void DowngradeToFree(DateTimeOffset? endedAt = null)
    {
        // Only a real subscription start the retention clock — a Free account that never paid
        // has no "subscription end" in the privacy-policy sense.
        if (Plan == CustomerPlan.Full) SubscriptionEndedAt = endedAt;
        ComplimentaryUntil = null;
        Plan = CustomerPlan.Free;
        StripeSubscriptionId = null;
        PlanRenewsAt = null;
        // StripeCustomerId intentionally retained.
    }
}

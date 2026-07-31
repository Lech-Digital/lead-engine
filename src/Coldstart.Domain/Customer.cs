namespace Coldstart.Domain;

public sealed class Customer
{
    private readonly List<Lead> _leads = new();

    public CustomerId Id { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public CompanyProfile Profile { get; private set; } = CompanyProfile.Empty;
    public CustomerPlan Plan { get; private set; } = CustomerPlan.Free;
    public HarvestStatus HarvestStatus { get; private set; } = HarvestStatus.None;
    public DateTimeOffset? LastHarvestAt { get; private set; }
    public string? StripeCustomerId { get; private set; }
    public string? StripeSubscriptionId { get; private set; }
    public DateTimeOffset? PlanRenewsAt { get; private set; }
    // Briefing push channels (customer-configured; null = channel off).
    public string? SlackWebhookUrl { get; private set; }
    public string? WhatsAppNumber { get; private set; }
    // Comma-separated source names the customer opted into; null = all sources.
    public string? EnabledSourcesCsv { get; private set; }
    public IReadOnlyList<Lead> Leads => _leads.AsReadOnly();

    private Customer() { } // EF materialization

    private Customer(CustomerId id, string name)
    {
        Id = id;
        Name = name;
    }

    public static Customer Create(string name) => new(CustomerId.New(), name);

    public void UpdateProfile(CompanyProfile profile) => Profile = profile ?? CompanyProfile.Empty;

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

    public void LinkStripeCustomer(string stripeCustomerId) => StripeCustomerId = stripeCustomerId;

    public void ActivateFull(string subscriptionId, DateTimeOffset? renewsAt)
    {
        Plan = CustomerPlan.Full;
        StripeSubscriptionId = subscriptionId;
        PlanRenewsAt = renewsAt;
    }

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

    public void DowngradeToFree()
    {
        Plan = CustomerPlan.Free;
        StripeSubscriptionId = null;
        PlanRenewsAt = null;
        // StripeCustomerId intentionally retained.
    }
}

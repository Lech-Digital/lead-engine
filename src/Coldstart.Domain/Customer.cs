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
}

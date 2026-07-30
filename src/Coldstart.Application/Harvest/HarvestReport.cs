namespace Coldstart.Application.Harvest;

public sealed record HarvestReport(int Harvested, int ScoredIn, int Deduped, int Enriched, int Saved, int Rejected,
    IReadOnlyList<SavedLead>? SavedLeads = null)
{
    public IReadOnlyList<SavedLead> NewLeads => SavedLeads ?? [];
}

// A lead the run just saved for review — enough for a push notification.
public sealed record SavedLead(Guid Id, string Company, string Subject);

namespace Coldstart.Application.Harvest;

public sealed record HarvestReport(int Harvested, int ScoredIn, int Deduped, int Enriched, int Saved, int Rejected,
    int Attempted = 0,
    IReadOnlyList<SavedLead>? SavedLeads = null,
    IReadOnlyDictionary<string, int>? DropReasons = null,
    IReadOnlyDictionary<string, int>? PerSource = null)
{
    public IReadOnlyList<SavedLead> NewLeads => SavedLeads ?? [];
    public IReadOnlyDictionary<string, int> DroppedBy => DropReasons ?? new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> SavedBySource => PerSource ?? new Dictionary<string, int>();
}

// A lead the run just saved for review — enough for a push notification.
public sealed record SavedLead(Guid Id, string Company, string Subject);

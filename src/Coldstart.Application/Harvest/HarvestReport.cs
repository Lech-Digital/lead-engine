namespace Coldstart.Application.Harvest;

public sealed record HarvestReport(int Harvested, int ScoredIn, int Deduped, int Enriched, int Saved);

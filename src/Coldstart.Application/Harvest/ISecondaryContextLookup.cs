namespace Coldstart.Application.Harvest;

// One extra public snippet about a company, fetched before enrichment so the draft
// has a second data point beyond the harvested item. Null when unavailable — the
// pipeline must always proceed without it.
public interface ISecondaryContextLookup
{
    Task<string?> LookupAsync(string company, CancellationToken ct);
}

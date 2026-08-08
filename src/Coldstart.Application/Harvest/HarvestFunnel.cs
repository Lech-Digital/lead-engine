namespace Coldstart.Application.Harvest;

// One run's funnel, counted as it happens. Attribution by reason is the point:
// for months every non-funding source was silently dropped at the score gate and
// the report could not show it, because it counted only totals.
public sealed class HarvestFunnel
{
    private readonly Dictionary<string, int> _dropReasons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _perSource = new(StringComparer.OrdinalIgnoreCase);

    public int Harvested { get; private set; }
    public int Vetoed { get; private set; }
    public int Prioritised { get; private set; }
    public int Triaged { get; private set; }
    public int Attempted { get; private set; }
    public int Enriched { get; private set; }
    public int Saved { get; private set; }
    public int Rejected { get; private set; }

    // Legacy counters — preserve the exact pre-funnel meaning of HarvestReport's ScoredIn/
    // Deduped positions, which live consumers (HarvestRunAlert's ScoredIn - Deduped arithmetic)
    // depend on. Since the score gate was removed (Task 3), ScoredIn = survived veto and was
    // ranked (same event as CountPrioritised, tracked separately on purpose) — every non-
    // vetoed candidate now reaches this point, not just the ones that used to clear MinScore.
    // Deduped = fingerprint-dedup drops ONLY — it must NOT become the full Vetoed aggregate,
    // which also includes suppressed/veto/post-enrichment drops that happen outside the
    // ScoredIn cohort and would make the subtraction go negative. ScoredIn - Deduped is now
    // the size of the actual candidate pool handed to phase 2 (pre-enrich-cap) — an accurate
    // count of that pool, whereas before it undercounted by however many the bogus score gate
    // silently ate. HarvestRunAlert's diagnosis (LLM failure vs content starvation) still holds.
    public int ScoredIn { get; private set; }
    public int Deduped { get; private set; }

    public IReadOnlyDictionary<string, int> DropReasons => _dropReasons;
    public IReadOnlyDictionary<string, int> PerSource => _perSource;

    public void CountHarvested() => Harvested++;
    public void CountPrioritised() { Prioritised++; ScoredIn++; }
    public void CountTriaged() => Triaged++;
    // One per actual call to the enricher (success or failure) — distinct from ScoredIn/
    // Deduped's candidate-pool size, because MaxEnrichPerRun caps how many of that pool are
    // ever sent. HarvestRunAlert needs this to avoid implying the whole pool was tried.
    public void CountAttempted() => Attempted++;
    public void CountEnriched() => Enriched++;
    public void CountRejected() => Rejected++;

    public void CountDropped(string reason)
    {
        Vetoed++;
        _dropReasons[reason] = _dropReasons.TryGetValue(reason, out var n) ? n + 1 : 1;
    }

    // Dedicated call for the two fingerprint-dedup sites: keeps the "duplicate-fingerprint"
    // reason string centralised here (no risk of a call site typo splitting the bucket) and
    // keeps the legacy Deduped counter in lockstep with the new attribution.
    public void CountDuplicateFingerprint()
    {
        Deduped++;
        CountDropped("duplicate-fingerprint");
    }

    public void CountSavedFrom(string source)
    {
        Saved++;
        _perSource[source] = _perSource.TryGetValue(source, out var n) ? n + 1 : 1;
    }

    public HarvestReport ToReport(IReadOnlyList<SavedLead> savedLeads) =>
        new(Harvested, ScoredIn, Deduped, Enriched, Saved, Rejected, Attempted: Attempted,
            SavedLeads: savedLeads, DropReasons: _dropReasons, PerSource: _perSource);
}

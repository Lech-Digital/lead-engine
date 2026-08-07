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
    public int Enriched { get; private set; }
    public int Saved { get; private set; }
    public int Rejected { get; private set; }

    public IReadOnlyDictionary<string, int> DropReasons => _dropReasons;
    public IReadOnlyDictionary<string, int> PerSource => _perSource;

    public void CountHarvested() => Harvested++;
    public void CountPrioritised() => Prioritised++;
    public void CountTriaged() => Triaged++;
    public void CountEnriched() => Enriched++;
    public void CountRejected() => Rejected++;

    public void CountDropped(string reason)
    {
        Vetoed++;
        _dropReasons[reason] = _dropReasons.TryGetValue(reason, out var n) ? n + 1 : 1;
    }

    public void CountSavedFrom(string source)
    {
        Saved++;
        _perSource[source] = _perSource.TryGetValue(source, out var n) ? n + 1 : 1;
    }

    public HarvestReport ToReport(IReadOnlyList<SavedLead> savedLeads) =>
        new(Harvested, Prioritised, Vetoed, Enriched, Saved, Rejected, savedLeads, _dropReasons, _perSource);
}

namespace Coldstart.Domain;

// One row per harvest run — the raw material for the public "engine ticker"
// and the operator cockpit. Aggregate telemetry only; no lead content.
public sealed class HarvestRunStat
{
    public Guid Id { get; private set; }
    public DateTimeOffset RanAt { get; private set; }
    public int Harvested { get; private set; }
    public int Saved { get; private set; }

    private HarvestRunStat() { } // EF materialization

    public static HarvestRunStat Record(DateTimeOffset ranAt, int harvested, int saved) =>
        new() { Id = Guid.NewGuid(), RanAt = ranAt, Harvested = harvested, Saved = saved };
}

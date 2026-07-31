namespace Coldstart.Application.Harvest;

public interface ILeadSource
{
    string Name { get; }
    double BaseScore { get; }
    double ScoreWeight { get; }
    bool Enabled { get; }
    // runNiche is the per-run targeting (per-customer keywords + global source config);
    // sources that search by keyword derive their queries from it.
    Task<IReadOnlyList<RawItem>> FetchAsync(NicheOptions runNiche, CancellationToken ct);
}

namespace Coldstart.Application.Harvest;

public interface ILeadSource
{
    string Name { get; }
    double BaseScore { get; }
    double ScoreWeight { get; }
    bool Enabled { get; }
    Task<IReadOnlyList<RawItem>> FetchAsync(CancellationToken ct);
}

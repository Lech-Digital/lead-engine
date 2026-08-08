namespace Coldstart.Application.Harvest;

// Stage 2: ordering, not gating. The old design dropped anything below MinScore, and
// because keyword matching never fired, only sources whose BASE score already cleared
// the bar survived — funding at 0.50 lived, web search 0.35 / Adzuna 0.35 /
// LangSearch 0.25 / Hacker News all died. Ranking lets every source compete for the
// same spend without an absolute bar.
internal static class CandidatePriority
{
    private const double SignalWeight = 0.6;
    private const double SourceWeight = 0.3;
    private const double RecencyWeight = 0.1;
    private static readonly TimeSpan RecencyHorizon = TimeSpan.FromDays(14);

    public static double Rank(SignalResult signal, double sourceBaseScore, DateTimeOffset? createdAt, DateTimeOffset now)
    {
        var signalPart = signal.Matched.Count > 0 ? signal.Score : 0;
        // No date is not evidence of staleness — give the neutral mid value rather than
        // zero, so a source that never emits dates isn't permanently last.
        var recencyPart = createdAt is { } created
            ? Math.Clamp(1 - (now - created).TotalDays / RecencyHorizon.TotalDays, 0, 1)
            : 0.5;

        return Math.Round(SignalWeight * signalPart + SourceWeight * sourceBaseScore + RecencyWeight * recencyPart, 4);
    }
}

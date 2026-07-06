namespace Coldstart.Application.Harvest;

public static class SignalScorer
{
    public static SignalResult Score(RawItem item, NicheOptions niche, double baseScore, double scoreWeight)
    {
        var haystack = $"{item.Title}\n{item.Text}".ToLowerInvariant();
        var targetHits = niche.TargetKeywords.Where(k => haystack.Contains(k.ToLowerInvariant())).ToList();
        var signalHits = niche.BuyingSignals.Where(s => haystack.Contains(s.ToLowerInvariant())).ToList();

        var keywordScore = scoreWeight * (0.5 * Saturating(targetHits.Count) + 0.5 * Saturating(signalHits.Count));
        var score = Math.Round(Math.Max(keywordScore, baseScore), 3);
        return new SignalResult(score, [.. targetHits, .. signalHits]);
    }

    // 0 hits -> 0.0, 1 -> 0.6, 2 -> 0.8, 3 -> 0.9, capped at 1.0
    private static double Saturating(int n) => n <= 0 ? 0.0 : Math.Min(1.0, 1.0 - 0.4 / n);
}

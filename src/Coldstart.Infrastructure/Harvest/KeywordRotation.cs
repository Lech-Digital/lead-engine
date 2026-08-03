namespace Coldstart.Infrastructure.Harvest;

// A metered search budget of one query a night must still cover the whole profile:
// the window start rotates by UTC day-of-year, so keyword N gets its turn every
// keywords.Count nights instead of the first keyword monopolising the budget.
internal static class KeywordRotation
{
    public static IEnumerable<string> Window(IReadOnlyList<string> keywords, int max, DateTimeOffset now)
    {
        if (keywords.Count == 0 || max <= 0) yield break;
        var start = now.UtcDateTime.DayOfYear % keywords.Count;
        for (var i = 0; i < Math.Min(max, keywords.Count); i++)
            yield return keywords[(start + i) % keywords.Count];
    }
}

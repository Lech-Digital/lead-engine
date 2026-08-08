namespace Coldstart.Application.Harvest;

// Stage 1 of the pipeline: drop only what is provably wrong, before any spend.
// Deliberately contains NO keyword scoring — using a quality heuristic as a cost
// control is what reduced this engine to a single source.
internal static class CandidateVeto
{
    public static string? Check(
        RawItem item,
        IReadOnlyList<string> exclusions,
        ISet<string> alreadyHeldCompanies,
        DateTimeOffset now,
        TimeSpan freshness)
    {
        if (PublisherDenylist.IsPublisherOrInvestor(item.Author)) return "publisher-or-investor";

        var haystack = $"{item.Title}\n{item.Text}\n{item.Author}".ToLowerInvariant();
        if (exclusions.Any(e => !string.IsNullOrWhiteSpace(e) && haystack.Contains(e.Trim().ToLowerInvariant(), StringComparison.Ordinal)))
            return "excluded";

        if (!string.IsNullOrWhiteSpace(item.Author) && alreadyHeldCompanies.Contains(item.Author.Trim()))
            return "duplicate-company";

        // An unparseable or absent date must never count as stale: several sources emit
        // no usable date, and treating that as old would silently delete whole sources.
        if (DateTimeOffset.TryParse(item.CreatedAt, out var created) && created < now - freshness)
            return "stale";

        return null;
    }
}

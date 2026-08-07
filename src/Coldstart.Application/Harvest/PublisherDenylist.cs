namespace Coldstart.Application.Harvest;

// Feeds routinely name the publisher or the investor rather than the funded company.
// Production showed tech.eu, news.crunchbase.com, Lightspeed Venture Partners, Menlo
// Ventures and White Star Capital all stored as prospects. Matching is substring-based
// on a normalised name so "Lightspeed Venture Partners" and "Lightspeed" both hit.
internal static class PublisherDenylist
{
    private static readonly string[] Publishers =
    [
        "techcrunch", "tech.eu", "crunchbase", "sifted", "eu-startups", "silicon canals",
        "venturebeat", "the next web", "businesswire", "pr newswire", "globenewswire",
    ];

    private static readonly string[] InvestorMarkers =
    [
        "ventures", "venture partners", "capital", "partners llp", "vc fund",
        "equity", "invest", "fund i", "fund ii", "seed fund",
    ];

    public static bool IsPublisherOrInvestor(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim().ToLowerInvariant();
        return Publishers.Any(p => n.Contains(p, StringComparison.Ordinal))
            || InvestorMarkers.Any(m => n.Contains(m, StringComparison.Ordinal));
    }
}

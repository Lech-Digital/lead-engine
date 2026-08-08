namespace Coldstart.Application.Harvest;

// Feeds routinely name the publisher or the investor rather than the funded company.
// Production showed tech.eu, news.crunchbase.com, Lightspeed Venture Partners, Menlo
// Ventures and White Star Capital all stored as prospects. The publisher list stays
// substring-based — domain-style names like "tech.eu" and "news.crunchbase.com" are
// unambiguous. Investor markers are split in two:
//   - InvestorPhrases: multi-word, already specific enough to be a safe substring match
//     ("venture partners", "partners llp", ...).
//   - InvestorSuffixes: bare company-form words ("ventures", "capital", "partners",
//     "equity", "invest") that are common inside GENUINE prospect names too — e.g. a
//     real fintech customer's own prospect list can contain "Human Capital Analytics",
//     "SmartInvest" or "Equity Payroll". This denylist is global across every customer,
//     and a wrongly vetoed prospect is invisible (it never reaches a human), so these
//     are matched only as a trailing WORD, not a bare substring: "White Star Capital"
//     ends with the word "capital" and is vetoed; "Human Capital Analytics" does not
//     end with it and is kept; "SmartInvest" has no word boundary before "invest" (it's
//     fused into "Smart") and is kept.
internal static class PublisherDenylist
{
    private static readonly string[] Publishers =
    [
        "techcrunch", "tech.eu", "crunchbase", "sifted", "eu-startups", "silicon canals",
        "venturebeat", "the next web", "businesswire", "pr newswire", "globenewswire",
    ];

    private static readonly string[] InvestorPhrases =
    [
        "venture partners", "partners llp", "vc fund", "seed fund", "fund i", "fund ii",
    ];

    private static readonly string[] InvestorSuffixes =
    [
        "ventures", "capital", "partners", "equity", "invest",
    ];

    public static bool IsPublisherOrInvestor(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim().ToLowerInvariant();
        return Publishers.Any(p => n.Contains(p, StringComparison.Ordinal))
            || InvestorPhrases.Any(m => n.Contains(m, StringComparison.Ordinal))
            || InvestorSuffixes.Any(s => EndsWithWord(n, s));
    }

    // True only when `marker` is the trailing WORD of `name`: either the whole name, or
    // preceded by a space. Rejects a marker merely fused onto the end of another word
    // ("smartinvest" does not end-with-word "invest").
    private static bool EndsWithWord(string name, string marker) =>
        name == marker || name.EndsWith(" " + marker, StringComparison.Ordinal);
}

namespace Coldstart.Application.Harvest;

// Pure helper: picks the best available company-naming key from a RawItem for the
// secondary-context lookup. ILeadSource implementations populate Author with wildly
// different things — a real company name (AdzunaSource), an HN username
// (HackerNewsSource), a publisher/site name (LangSearchSource, WebSearchSource), or —
// critically — the RSS feed's own URL host, a constant shared by every item from that
// feed (FundingRssSource, DutchBusinessNewsSource: e.g. "techcrunch.com" for every single
// TechCrunch funding blurb). Author can't be trusted blindly, so this rejects the
// not-a-company shapes and falls back to Title, which across every source is the richest
// pre-enrichment field and, for a funding item ("Acme raises $50M Series A"), actually
// names the company and the event.
internal static class SecondaryContextKey
{
    public static string? For(RawItem item)
    {
        if (IsPlausibleCompanyName(item.Author)) return item.Author.Trim();
        if (!string.IsNullOrWhiteSpace(item.Title)) return item.Title.Trim();
        return null;
    }

    // A real company name is either multi-word ("Acme Corp", "The Verge") or, when a
    // single token, carries at least one uppercase letter ("ASML", "Vestbee" — and,
    // importantly, "Booking.com": a real company whose legal name itself contains a dot,
    // which a naive "contains a dot" domain check would wrongly reject). Anything else
    // single-token is either a bare feed/URL host — Uri.Host always normalizes to
    // lowercase, so "techcrunch.com" never carries an uppercase letter — or a handle:
    // HN usernames are conventionally all-lowercase ("pg", "dang", "sama"). Neither names
    // a company, so both fall through to the Title fallback.
    private static bool IsPlausibleCompanyName(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim();
        return t.Any(char.IsWhiteSpace) || t.Any(char.IsUpper);
    }
}

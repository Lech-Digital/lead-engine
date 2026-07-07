namespace Coldstart.Application.Leads;

// Port of leadengine/contact.py — the genuine missing 20%: turning a company-level
// signal into "who do I contact and how do I find them." We deliberately produce
// RESEARCH LINKS, never fabricated email addresses — a guessed address tanks
// deliverability and breaks the "never invent facts" rule. These links drop you
// onto the exact people-search for the likely buyer.
public static class ContactLinkBuilder
{
    private const string DefaultBuyerTitle = "Head of Growth";
    private static readonly HashSet<string> Unusable = new(StringComparer.OrdinalIgnoreCase) { "-", "unknown", "n/a" };

    // Honest, actionable people-search links — never a fabricated address.
    public static IReadOnlyList<ContactLink> Build(string company, string sourceUrl, string buyerTitle = DefaultBuyerTitle)
    {
        company = (company ?? "").Trim();
        if (company.Length == 0 || Unusable.Contains(company)) return [];

        var links = new List<ContactLink>
        {
            new($"Find buyer ({buyerTitle})",
                "https://www.linkedin.com/search/results/people/?keywords=" + Uri.EscapeDataString($"{company} {buyerTitle}")),
            new("Web search",
                "https://www.google.com/search?q=" + Uri.EscapeDataString($"{company} {buyerTitle} LinkedIn")),
        };
        if (!string.IsNullOrWhiteSpace(sourceUrl))
            links.Add(new ContactLink("Signal source", sourceUrl));
        return links;
    }
}

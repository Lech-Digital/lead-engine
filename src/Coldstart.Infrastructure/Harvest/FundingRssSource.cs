using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

internal sealed class FundingRssSource(HttpClient http, NicheOptions niche) : ILeadSource
{
    private const string Sentinel =
        "FUNDING SIGNAL — this company recently announced new investment, a strong " +
        "indicator they are about to scale headcount and invest in new tooling:";
    private static readonly Regex FundingRe = new(
        @"\brais(?:e|es|ed|ing)\b|\b(?:pre-)?seed\b|\bseries\s+[a-h]\b|\bfunding\b|\bround\b|\bled\sby\b|\$\d+(?:\.\d+)?\s*[mMbB]\b|\blands?\s+\$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly FundingOptions _o = niche.Funding;

    public string Name => "funding";
    public double BaseScore => _o.BaseScore;
    public double ScoreWeight => 1.0;
    public bool Enabled => _o.Enabled;

    public async Task<IReadOnlyList<RawItem>> FetchAsync(CancellationToken ct)
    {
        var items = new List<RawItem>();
        foreach (var feed in _o.Feeds)
        {
            string xml;
            try { xml = await http.GetStringAsync(feed, ct); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { continue; }
            items.AddRange(ItemsFromFeed(xml, feed, DateTimeOffset.UtcNow));
        }
        return items;
    }

    private IEnumerable<RawItem> ItemsFromFeed(string xml, string feedUrl, DateTimeOffset now)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); } catch (System.Xml.XmlException) { yield break; }
        var cutoff = now.AddDays(-_o.LookbackDays);
        var label = Uri.TryCreate(feedUrl, UriKind.Absolute, out var u) ? u.Host : "feed";

        var entries = doc.Descendants().Where(e => Local(e.Name) is "item" or "entry").Take(_o.MaxPerFeed);
        foreach (var node in entries)
        {
            string title = "", url = "", summary = "", ident = "";
            DateTimeOffset? published = null;
            foreach (var c in node.Elements())
            {
                switch (Local(c.Name))
                {
                    case "title": title = Clean(c.Value); break;
                    case "link": url = string.IsNullOrWhiteSpace(c.Value) ? (c.Attribute("href")?.Value ?? "") : c.Value.Trim(); break;
                    case "description" or "summary" or "content" when summary == "": summary = Clean(c.Value); break;
                    case "guid" or "id" when ident == "": ident = c.Value.Trim(); break;
                    case "pubdate" or "published" or "updated" when published is null: published = ParseDate(c.Value); break;
                }
            }
            if (published is not null && published < cutoff) continue;
            if (_o.RequireFundingTerms && !FundingRe.IsMatch($"{title} {summary}")) continue;
            var id = !string.IsNullOrEmpty(ident) ? ident : (!string.IsNullOrEmpty(url) ? url : title);
            yield return new RawItem("funding", $"{label}:{id}",
                title, $"{Sentinel} {title}. {summary}".Trim(), label, url,
                published?.ToString("o") ?? "");
        }
    }

    private static string Local(XName n) => n.LocalName.ToLowerInvariant();

    private static DateTimeOffset? ParseDate(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        if (DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            return dt;
        return null;
    }

    private static string Clean(string? s)
    {
        s = Regex.Replace(s ?? "", "<[^>]+>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(s), @"\s+", " ").Trim();
    }
}

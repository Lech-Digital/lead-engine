using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

// Targeted week-fresh web search via the Brave Search API: every run searches the RUN's own
// profile keywords — a far wider net than the curated RSS feeds, same honest framing.
// (Google's Custom Search JSON API closed to new customers in 2026 and retires 2027-01-01.)
// Free tier: 2,000 queries/month. Soft-degrades to off without a key, like Adzuna.
internal sealed class WebSearchSource(HttpClient http, NicheOptions niche) : ILeadSource
{
    internal const string Sentinel =
        "WEB SIGNAL — this page about the company was published or updated this week, a " +
        "concrete, current indicator of what is happening there right now:";

    private readonly WebSearchOptions _o = niche.WebSearch;

    public string Name => "websearch";
    public double BaseScore => _o.BaseScore;
    public double ScoreWeight => _o.ScoreWeight;
    public bool Enabled => _o.Enabled && !string.IsNullOrWhiteSpace(_o.ApiKey);

    public async Task<IReadOnlyList<RawItem>> FetchAsync(NicheOptions runNiche, CancellationToken ct)
    {
        var items = new List<RawItem>();
        foreach (var keyword in runNiche.TargetKeywords.Take(_o.MaxQueries))
        {
            var url = "https://api.search.brave.com/res/v1/web/search" +
                      $"?q={Uri.EscapeDataString(keyword)}&count={Math.Clamp(_o.ResultsPerQuery, 1, 20)}" +
                      $"&country={Uri.EscapeDataString(_o.Country)}&freshness={Uri.EscapeDataString(_o.Freshness)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Subscription-Token", _o.ApiKey);
            request.Headers.Add("Accept", "application/json");
            try
            {
                using var response = await http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) continue;   // quota/key trouble must not kill the run
                var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                if (!root.TryGetProperty("web", out var web) ||
                    !web.TryGetProperty("results", out var hits) || hits.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var hit in hits.EnumerateArray())
                {
                    var title = Str(hit, "title");
                    var link = Str(hit, "url");
                    var description = Str(hit, "description");
                    var age = Str(hit, "age");
                    var site = hit.TryGetProperty("profile", out var p) ? Str(p, "name") : "";
                    if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(link)) continue;
                    items.Add(new RawItem("websearch", link, title,
                        $"{Sentinel} {site}: {title}. {description}".Trim(),
                        site, link, age));
                }
            }
            catch
            {
                // A failing query must not abort the run — the next keyword still fetches.
            }
        }
        return items;
    }

    private static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

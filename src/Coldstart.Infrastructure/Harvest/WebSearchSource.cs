using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

// Targeted week-fresh web search via Scrapingdog's Google Search API: every run searches
// the RUN's own profile keywords — a far wider net than the curated RSS feeds, same honest
// framing. (Brave requires a credit card; Google's own Custom Search JSON API closed to new
// customers in 2026.) The `tbs=qdr:w` week filter is enforced by Google itself, which is
// what keeps the Sentinel's "this week" claim true. Free tier: 200 credits/month at 5 per
// search → ~40 searches; MaxQueries defaults to 1 with day-rotation to stay inside that.
// Soft-degrades to off without a key, like Adzuna. NOTE: the endpoint is /search — the
// documented /google/ path returns a different, organic-less payload.
internal sealed class WebSearchSource : ILeadSource
{
    internal const string Sentinel =
        "WEB SIGNAL — this page about the company was published or updated this week, a " +
        "concrete, current indicator of what is happening there right now:";

    private readonly HttpClient _http;
    private readonly WebSearchOptions _o;
    private readonly Func<DateTimeOffset> _clock;

    public WebSearchSource(HttpClient http, NicheOptions niche)
        : this(http, niche, () => DateTimeOffset.UtcNow) { }

    // Test seam: a fixed clock makes the day-of-year keyword rotation deterministic.
    internal WebSearchSource(HttpClient http, NicheOptions niche, Func<DateTimeOffset> clock)
    {
        _http = http;
        _o = niche.WebSearch;
        _clock = clock;
    }

    public string Name => "websearch";
    public double BaseScore => _o.BaseScore;
    public double ScoreWeight => _o.ScoreWeight;
    public bool Enabled => _o.Enabled && !string.IsNullOrWhiteSpace(_o.ApiKey);

    public async Task<IReadOnlyList<RawItem>> FetchAsync(NicheOptions runNiche, CancellationToken ct)
    {
        var items = new List<RawItem>();
        foreach (var keyword in KeywordRotation.Window(runNiche.TargetKeywords, _o.MaxQueries, _clock()))
        {
            var url = "https://api.scrapingdog.com/search" +
                      $"?api_key={Uri.EscapeDataString(_o.ApiKey)}&query={Uri.EscapeDataString(keyword)}" +
                      $"&results={Math.Clamp(_o.ResultsPerQuery, 1, 20)}" +
                      $"&country={Uri.EscapeDataString(_o.Country)}&tbs={Uri.EscapeDataString(_o.Freshness)}";
            try
            {
                using var response = await _http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) continue;   // quota/key trouble must not kill the run
                var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                if (!root.TryGetProperty("organic_results", out var hits) || hits.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var hit in hits.EnumerateArray())
                {
                    var title = Str(hit, "title");
                    var link = Str(hit, "link");
                    var snippet = Str(hit, "snippet");
                    var site = Str(hit, "source") is { Length: > 0 } s ? s : Str(hit, "displayed_link");
                    if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(link)) continue;
                    items.Add(new RawItem("websearch", link, title,
                        $"{Sentinel} {site}: {title}. {snippet}".Trim(),
                        site, link, ""));
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

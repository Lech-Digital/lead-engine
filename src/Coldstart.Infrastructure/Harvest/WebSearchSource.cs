using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

// Targeted week-fresh web search via Google's official Custom Search JSON API: every run
// searches the RUN's own profile keywords — a far wider net than the curated RSS feeds,
// same honest framing. Free tier (100 queries/day) covers nightly harvests comfortably.
// Soft-degrades to off without a key + cx, like Adzuna.
internal sealed class WebSearchSource(HttpClient http, NicheOptions niche) : ILeadSource
{
    internal const string Sentinel =
        "WEB SIGNAL — this page about the company was published or updated this week, a " +
        "concrete, current indicator of what is happening there right now:";

    private readonly WebSearchOptions _o = niche.WebSearch;

    public string Name => "websearch";
    public double BaseScore => _o.BaseScore;
    public double ScoreWeight => _o.ScoreWeight;
    public bool Enabled => _o.Enabled && !string.IsNullOrWhiteSpace(_o.ApiKey) && !string.IsNullOrWhiteSpace(_o.Cx);

    public async Task<IReadOnlyList<RawItem>> FetchAsync(NicheOptions runNiche, CancellationToken ct)
    {
        var items = new List<RawItem>();
        foreach (var keyword in runNiche.TargetKeywords.Take(_o.MaxQueries))
        {
            var url = "https://www.googleapis.com/customsearch/v1" +
                      $"?key={Uri.EscapeDataString(_o.ApiKey)}&cx={Uri.EscapeDataString(_o.Cx)}" +
                      $"&q={Uri.EscapeDataString(keyword)}&num={Math.Clamp(_o.ResultsPerQuery, 1, 10)}" +
                      $"&dateRestrict={Uri.EscapeDataString(_o.DateRestrict)}&gl={Uri.EscapeDataString(_o.Gl)}";
            try
            {
                using var response = await http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) continue;   // quota/key trouble must not kill the run
                var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                if (!root.TryGetProperty("items", out var hits) || hits.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var hit in hits.EnumerateArray())
                {
                    var title = Str(hit, "title");
                    var link = Str(hit, "link");
                    var snippet = Str(hit, "snippet");
                    var site = Str(hit, "displayLink");
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

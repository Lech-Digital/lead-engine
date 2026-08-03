using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

// Free, unmetered wide-net web search via LangSearch — a background-evidence complement to
// the metered week-fresh websearch source. Its freshness filter is decorative (verified
// 2026-08-03: `oneDay` and `noLimit` return near-identical, months-old results and
// datePublished is always null), so the Sentinel must NEVER claim recency: a "congrats on
// the raise" draft about a nine-month-old round is exactly the email Coldstart must never
// write. `freshness: oneWeek` is still sent — harmless today, free upside if it ever works.
// Soft-degrades to off without a key.
internal sealed class LangSearchSource : ILeadSource
{
    internal const string Sentinel =
        "WEB RESULT — a public page about the company that matches the sender's target " +
        "keywords. Publication date UNKNOWN — it may be months old: never congratulate or " +
        "treat any event it describes as current; use it only as background evidence of fit.";

    private readonly HttpClient _http;
    private readonly LangSearchOptions _o;
    private readonly Func<DateTimeOffset> _clock;

    public LangSearchSource(HttpClient http, NicheOptions niche)
        : this(http, niche, () => DateTimeOffset.UtcNow) { }

    // Test seam: a fixed clock makes the day-of-year keyword rotation deterministic.
    internal LangSearchSource(HttpClient http, NicheOptions niche, Func<DateTimeOffset> clock)
    {
        _http = http;
        _o = niche.LangSearch;
        _clock = clock;
    }

    public string Name => "langsearch";
    public double BaseScore => _o.BaseScore;
    public double ScoreWeight => _o.ScoreWeight;
    public bool Enabled => _o.Enabled && !string.IsNullOrWhiteSpace(_o.ApiKey);

    public async Task<IReadOnlyList<RawItem>> FetchAsync(NicheOptions runNiche, CancellationToken ct)
    {
        var items = new List<RawItem>();
        foreach (var keyword in KeywordRotation.Window(runNiche.TargetKeywords, _o.MaxQueries, _clock()))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.langsearch.com/v1/web-search")
            {
                Content = JsonContent.Create(new
                {
                    query = keyword,
                    freshness = "oneWeek",
                    count = Math.Clamp(_o.Count, 1, 10),
                }),
            };
            request.Headers.Add("Authorization", $"Bearer {_o.ApiKey}");
            try
            {
                using var response = await _http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) continue;   // key trouble must not kill the run
                var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                if (!root.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("webPages", out var pages) ||
                    !pages.TryGetProperty("value", out var hits) || hits.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var hit in hits.EnumerateArray())
                {
                    var name = Str(hit, "name");
                    var link = Str(hit, "url");
                    var snippet = Str(hit, "snippet");
                    var site = Str(hit, "siteName");
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(link)) continue;
                    items.Add(new RawItem("langsearch", link, name,
                        $"{Sentinel} {site}: {name}. {snippet}".Trim(),
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

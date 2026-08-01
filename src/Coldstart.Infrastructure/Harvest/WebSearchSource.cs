using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

// Targeted news search via serper.dev (Google results): every run searches the RUN's own
// profile keywords against this week's news — far wider net than the curated RSS feeds,
// same honest framing. Soft-degrades to off without an API key.
internal sealed class WebSearchSource(HttpClient http, NicheOptions niche) : ILeadSource
{
    internal const string Sentinel =
        "NEWS SIGNAL — this company appeared in the news this week, a concrete, current " +
        "indicator of what is happening there right now:";

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
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://google.serper.dev/news");
            request.Headers.Add("X-API-KEY", _o.ApiKey);
            request.Content = JsonContent.Create(new
            {
                q = keyword,
                gl = _o.Gl,
                hl = _o.Hl,
                num = _o.ResultsPerQuery,
                tbs = _o.Freshness,
            });
            try
            {
                using var response = await http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) continue;   // quota/key trouble must not kill the run
                var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                if (!root.TryGetProperty("news", out var news) || news.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var hit in news.EnumerateArray())
                {
                    var title = Str(hit, "title");
                    var link = Str(hit, "link");
                    var snippet = Str(hit, "snippet");
                    var outlet = Str(hit, "source");
                    var date = Str(hit, "date");
                    if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(link)) continue;
                    items.Add(new RawItem("websearch", link, title,
                        $"{Sentinel} {outlet}: {title}. {snippet}".Trim(),
                        outlet, link, date));
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

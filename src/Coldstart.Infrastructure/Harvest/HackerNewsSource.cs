using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

internal sealed class HackerNewsSource(HttpClient http, NicheOptions niche) : ILeadSource
{
    private const string Algolia = "https://hn.algolia.com/api/v1/search_by_date";
    private readonly HackerNewsOptions _o = niche.HackerNews;

    public string Name => "hackernews";
    public double BaseScore => 0;
    public double ScoreWeight => _o.ScoreWeight;
    public bool Enabled => _o.Enabled;

    public async Task<IReadOnlyList<RawItem>> FetchAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-_o.LookbackHours).ToUnixTimeSeconds();
        var items = new List<RawItem>();
        foreach (var query in _o.Queries)
        {
            var url = $"{Algolia}?query={Uri.EscapeDataString(query)}&tags=comment" +
                      $"&numericFilters=created_at_i>{cutoff}&hitsPerPage={_o.MaxPerQuery}";
            try
            {
                var root = await http.GetFromJsonAsync<JsonElement>(url, ct);
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("hits", out var hits)
                    || hits.ValueKind != JsonValueKind.Array)
                {
                    continue;   // one odd response must not kill the run
                }
                foreach (var h in hits.EnumerateArray())
                {
                    var text = Str(h, "comment_text");
                    if (string.IsNullOrEmpty(text)) text = Str(h, "story_text");
                    if (string.IsNullOrEmpty(text)) continue;
                    var oid = Str(h, "objectID");
                    items.Add(new RawItem("hackernews", oid, Str(h, "story_title"), StripHtml(text),
                        Str(h, "author"), $"https://news.ycombinator.com/item?id={oid}", Str(h, "created_at")));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                continue;   // one bad query must not kill the run
            }
        }
        return items;
    }

    private static string Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string StripHtml(string s)
    {
        s = Regex.Replace(s, "<[^>]+>", " ");
        s = System.Net.WebUtility.HtmlDecode(s);
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}

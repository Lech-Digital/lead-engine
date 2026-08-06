using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

// Fetches one extra public snippet about the company (own announcement, founder quote)
// via the free LangSearch API. Best-effort: any failure, missing key, or the config
// off-switch yields null and the pipeline proceeds on the primary item alone.
//
// Shares LangSearchSource's honesty lesson (verified 2026-08-03: LangSearch's freshness
// filter is decorative and datePublished is always null) — the preamble explicitly warns
// the model never to treat this snippet as current, so a draft never congratulates
// someone on a nine-month-old event.
internal sealed class LangSearchSecondaryLookup(HttpClient http, NicheOptions niche) : ISecondaryContextLookup
{
    internal const string Preamble =
        "SECONDARY CONTEXT (an additional public snippet about the same company; " +
        "publication date unknown — background evidence only, same rules: never treat " +
        "it as current news, never invent beyond it):";

    public async Task<string?> LookupAsync(string company, CancellationToken ct)
    {
        var o = niche.LangSearch;
        if (!o.SecondaryContextEnabled || string.IsNullOrWhiteSpace(o.ApiKey) || string.IsNullOrWhiteSpace(company))
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.langsearch.com/v1/web-search")
        {
            Content = JsonContent.Create(new { query = $"\"{company.Trim()}\" announcement OR blog OR interview", count = 3 }),
        };
        request.Headers.Add("Authorization", $"Bearer {o.ApiKey}");
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (!root.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("webPages", out var pages) ||
                !pages.TryGetProperty("value", out var hits) || hits.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var hit in hits.EnumerateArray())
            {
                var name = hit.TryGetProperty("name", out var n) ? n.GetString() : null;
                var snippet = hit.TryGetProperty("snippet", out var s) ? s.GetString() : null;
                if (string.IsNullOrWhiteSpace(snippet)) continue;
                return $"{Preamble}\n{name}: {snippet}".Trim();
            }
            return null;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;   // best-effort by contract
        }
    }
}

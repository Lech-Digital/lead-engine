using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

// Job-posting signals from the Adzuna search API (NL by default): a company hiring for a
// telling role is a concrete, dated buying signal. Queries come from the RUN's targeting,
// so every customer searches for their own keywords — not a global list.
internal sealed class AdzunaSource(HttpClient http, NicheOptions niche) : ILeadSource
{
    // Honest framing fed verbatim to the qualifier, mirroring the funding source's sentinel.
    internal const string Sentinel =
        "HIRING SIGNAL — this company just published this job opening, a concrete, current " +
        "indicator of what the team needs right now:";

    private readonly AdzunaOptions _o = niche.Adzuna;

    public string Name => "adzuna";
    public double BaseScore => _o.BaseScore;
    public double ScoreWeight => _o.ScoreWeight;
    public bool Enabled => _o.Enabled && !string.IsNullOrWhiteSpace(_o.AppId) && !string.IsNullOrWhiteSpace(_o.AppKey);

    public async Task<IReadOnlyList<RawItem>> FetchAsync(NicheOptions runNiche, CancellationToken ct)
    {
        var items = new List<RawItem>();
        foreach (var keyword in runNiche.TargetKeywords.Take(_o.MaxQueries))
        {
            // Multi-word keywords search as an exact phrase — Adzuna's plain `what` matching
            // is fuzzy enough to return unrelated Dutch job ads.
            var param = keyword.Contains(' ') ? "what_phrase" : "what";
            var url = $"https://api.adzuna.com/v1/api/jobs/{_o.Country}/search/1" +
                      $"?app_id={Uri.EscapeDataString(_o.AppId)}&app_key={Uri.EscapeDataString(_o.AppKey)}" +
                      $"&{param}={Uri.EscapeDataString(keyword)}&results_per_page={_o.ResultsPerQuery}" +
                      $"&max_days_old={_o.MaxDaysOld}&content-type=application/json";
            try
            {
                var root = await http.GetFromJsonAsync<JsonElement>(url, ct);
                if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                    continue;   // one odd response must not kill the run
                foreach (var job in results.EnumerateArray())
                {
                    var id = Str(job, "id");
                    var title = Str(job, "title");
                    var description = Str(job, "description");
                    var jobUrl = Str(job, "redirect_url");
                    var created = Str(job, "created");
                    var company = job.TryGetProperty("company", out var c) ? Str(c, "display_name") : "";
                    var location = job.TryGetProperty("location", out var l) ? Str(l, "display_name") : "";
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(company)) continue;
                    items.Add(new RawItem("adzuna", id, title,
                        $"{Sentinel} {company} is hiring: {title} ({location}). {description}".Trim(),
                        company, jobUrl, created));
                }
            }
            catch
            {
                // A failing query must not abort the run — the next keyword still fetches.
            }
        }
        return items;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

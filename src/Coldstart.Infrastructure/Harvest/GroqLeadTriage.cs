using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Harvest;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Harvest;

// Cheap first pass before the expensive GroqLeadEnricher call: a tiny, four-label verdict
// instead of a full draft. Production paid for a full enrichment call on 73% of candidates
// that the LLM then rejected anyway — this stage exists to catch that earlier, at roughly a
// tenth of the cost, because the output has no prose to generate.
internal sealed class GroqLeadTriage : ILeadTriage
{
    private const string System =
        "You are a fast, cheap triage filter for a B2B lead pipeline. You read one harvested " +
        "item and a customer's ICP criteria, and return ONLY a verdict — never prose, never an " +
        "email draft, never an explanation. You are strict about entities: a publisher writing " +
        "about a deal is not the deal's company, and an investor funding a company is not the " +
        "company either. You would rather say UNSURE than invent a match or guess a name.";

    // Retry ceiling for rate-limit / transient responses — mirrors GroqLeadEnricher so this
    // stage inherits the same resilience against the free-tier's 429s.
    private const int MaxRetries = 3;
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly LlmOptions _o;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    // Assumes sequential JudgeAsync calls within a scoped instance (the harvest loop is
    // sequential; not thread-safe by design) — same assumption as GroqLeadEnricher.
    private bool _sentBefore;

    public GroqLeadTriage(HttpClient http, IOptions<LlmOptions> options)
        : this(http, options, Task.Delay) { }

    // Test seam: inject a no-op / capturing delay so retry paths run without real waits.
    internal GroqLeadTriage(HttpClient http, IOptions<LlmOptions> options, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _http = http;
        _o = options.Value;
        _delay = delay;
    }

    public async Task<TriageVerdict?> JudgeAsync(RawItem item, string criteria, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_o.ApiKey)) return null;
        // Pace the batch: pause before every call after the first so a sequential run
        // stays under the provider rate limit instead of relying on 429 retries.
        if (_sentBefore && _o.RequestDelaySeconds > 0)
            await _delay(TimeSpan.FromSeconds(_o.RequestDelaySeconds), ct);
        _sentBefore = true;
        var prompt = BuildPrompt(item, criteria);
        var body = new
        {
            model = _o.Model,
            temperature = 0.2,
            messages = new[]
            {
                new { role = "system", content = System },
                new { role = "user", content = prompt },
            },
        };
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                // A fresh request per attempt — an HttpRequestMessage cannot be re-sent.
                using var req = new HttpRequestMessage(HttpMethod.Post, _o.BaseUrl)
                { Content = JsonContent.Create(body) };
                req.Headers.Add("Authorization", $"Bearer {_o.ApiKey}");
                using var resp = await _http.SendAsync(req, ct);
                if (IsTransient(resp.StatusCode) && attempt < MaxRetries)
                {
                    await _delay(BackoffFor(resp, attempt), ct);
                    continue;   // rate-limited / transient → back off and retry within this run
                }
                if (!resp.IsSuccessStatusCode) return null;
                var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
                if (!json.TryGetProperty("choices", out var choices)
                    || choices.ValueKind != JsonValueKind.Array
                    || choices.GetArrayLength() == 0) return null;
                var first = choices[0];
                if (!first.TryGetProperty("message", out var message)
                    || !message.TryGetProperty("content", out var contentEl)) return null;
                var content = contentEl.GetString();
                return string.IsNullOrWhiteSpace(content) ? null : TriageVerdictParser.Parse(content);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException
                or KeyNotFoundException or IndexOutOfRangeException)
            {
                return null;   // non-retryable transient → skip (retried next run), never crash the batch
            }
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    // Honor a server Retry-After (capped), else exponential backoff: 1s, 2s, 4s…
    private static TimeSpan BackoffFor(HttpResponseMessage resp, int attempt)
    {
        if (resp.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta < MaxBackoff ? delta : MaxBackoff;
        var seconds = Math.Min(Math.Pow(2, attempt), MaxBackoff.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private static string BuildPrompt(RawItem item, string criteria) => $"""
        CUSTOMER'S ICP CRITERIA:
        {criteria}

        HARVESTED ITEM (source: {item.Source} · author: {item.Author}):
        \"\"\"
        {item.Text}
        \"\"\"

        Judge whether this item is about a real B2B prospect that fits the criteria above.

        CRITICAL — the company name must be the REAL company or organisation the item is
        about, never the entity that merely published or wrote about it, and never an
        investor or VC firm. Production has repeatedly stored publishers (e.g. tech.eu,
        news.crunchbase.com) and VC firms (e.g. Lightspeed Venture Partners, Menlo Ventures)
        as if they were the prospect — do not repeat that mistake. If the item is really
        about the investor or the publisher and no underlying company is named, that is
        itself a reason to answer NOT FIT or UNSURE — never name the investor or publisher
        as the company just to fill in an answer.

        If the content is too thin to judge confidently, answer UNSURE rather than guessing.

        Return EXACTLY these four lines, nothing else — no explanation, no draft:

        VERDICT: FIT | NOT FIT | UNSURE
        COMPANY: the real company/org name, or "-" if it cannot be determined
        MET: comma-separated criteria that matched, or "none"
        FAILED: comma-separated criteria that failed or were missing, or "none"
        """;
}

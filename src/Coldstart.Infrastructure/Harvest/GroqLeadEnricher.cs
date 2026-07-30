using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Harvest;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Harvest;

internal sealed class GroqLeadEnricher : ILeadEnricher
{
    private const string System =
        "You are a top-performing B2B SDR known for replies, not blasts. You read one piece of " +
        "public web content, judge honestly whether the author/company is a real fit for a " +
        "specific offer, and if so write outreach so specific it could not have been sent to " +
        "anyone else. Every email opens in the prospect's world — what they are doing and the " +
        "pressure they are under — never with your own product. You write like a sharp person " +
        "typing to one human: short, clean sentences, one idea each, no marketing voice. You " +
        "never invent facts. You would rather say NO than send a generic pitch.";

    // Retry ceiling for rate-limit / transient responses. On the free tier a batch
    // otherwise loses ~2/3 of its calls to 429s that are silently dropped.
    private const int MaxRetries = 3;
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly LlmOptions _o;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    // Assumes sequential EnrichAsync calls within a scoped instance (the harvest loop is
    // sequential; not thread-safe by design).
    private bool _sentBefore;

    public GroqLeadEnricher(HttpClient http, IOptions<LlmOptions> options)
        : this(http, options, Task.Delay) { }

    // Test seam: inject a no-op / capturing delay so retry paths run without real waits.
    internal GroqLeadEnricher(HttpClient http, IOptions<LlmOptions> options, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _http = http;
        _o = options.Value;
        _delay = delay;
    }

    public async Task<EnrichmentResult?> EnrichAsync(RawItem item, NicheOptions niche, SignalResult signal, SenderContext sender, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_o.ApiKey)) return null;
        // Pace the batch: pause before every call after the first so a sequential run
        // stays under the provider rate limit instead of relying on 429 retries.
        if (_sentBefore && _o.RequestDelaySeconds > 0)
            await _delay(TimeSpan.FromSeconds(_o.RequestDelaySeconds), ct);
        _sentBefore = true;
        var prompt = BuildPrompt(item, niche, signal, sender);
        var body = new
        {
            model = _o.Model,
            temperature = 0.4,
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
                return string.IsNullOrWhiteSpace(content) ? null : EnrichmentParser.Parse(content);
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

    // Ported from enrich.py PROMPT_TEMPLATE.
    private static string BuildPrompt(RawItem item, NicheOptions n, SignalResult signal, SenderContext sender) =>
        $"""
        OFFER WE SELL:
        {n.Offer}
        {(string.IsNullOrWhiteSpace(sender.Brief) ? "" : $"""

        WHO IS REACHING OUT (the sender's own company — use only for credibility, in at most one short clause; the email is about the prospect, not the sender):
        {sender.Brief}
        """)}

        WHO IS A GOOD FIT:
        (niche described by keywords: {string.Join(", ", n.TargetKeywords)})

        WHO IS NOT A FIT — mark FIT: NO if ANY of these apply, even when keywords match:
        {(n.Disqualifiers.Count == 0 ? "(none specified)" : string.Join("\n", n.Disqualifiers.Select(d => "- " + d)))}

        HARVESTED CONTENT (source: {item.Source} · author: {item.Author}):
        \"\"\"
        {item.Text}
        \"\"\"

        Pre-detected signals (keyword-level, may be noisy): {string.Join(", ", signal.Matched)}

        Return EXACTLY these five sections, each on its own line with the label:

        FIT: YES | MAYBE | NO — would this specific person plausibly buy the offer? Be strict; default to NO if the content is off-topic, a job seeker, a competitor, or just noise.
        SUMMARY: one sentence — who they are and the concrete reason they do or don't fit.
        SUBJECT: a 4-7 word subject line referencing their specific situation (no clickbait, no emojis).
        DRAFT: a short, warm email from {n.SenderName} ({n.SenderTitle}), written to one person — 5-7 short sentences in total, laid out as a greeting line plus 2-3 paragraphs separated by ONE blank line each:
        - Greeting on its own line: "Hi <first name>," when the author's real first name is clearly visible in the content; otherwise exactly "Hi there,". Never guess or invent a name.
        - Paragraph 1: a specific detail from THEIR content that proves you actually read it, and the concrete need or pressure that detail creates for them right now.
        - Paragraph 2: the specific outcome the offer gives them for that need — a result they'd want, not a feature list.
        - Final paragraph: one friendly, low-effort question that is easy to answer with a single sentence. If the OFFER includes anything free or no-commitment (a sample, a trial, free leads), the question must offer exactly that — it is the easiest possible yes.
        Welcoming and human, never salesy: short, clean sentences, one idea each — no comma splices, no filler, no marketing voice. No signature (one is added on send). Banned: "I hope this email finds you well", "I came across", "reaching out", "game-changer", "revolutionize", "synergy", "circle back", "touch base", "in today's fast-paced".
        COMPANY: the real company/org name this is about, or "-" if it cannot be determined. Never guess.

        If FIT is NO, still fill SUBJECT and DRAFT with "-".
        {(sender.MentionSourceTool ? """

        Also: inside paragraph 2, state plainly in first person that the sender found this prospect using Coldstart — the very tool this email offers them (for example: "I found your post with Coldstart, the same engine I'd like to put to work for you."). Exactly one sentence; honest proof, stated as a simple fact, no boasting beyond the fact itself.
        """ : "")}
        """;
}

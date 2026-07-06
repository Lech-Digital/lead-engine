using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Harvest;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Harvest;

internal sealed class GroqLeadEnricher(HttpClient http, IOptions<LlmOptions> options) : ILeadEnricher
{
    private const string System =
        "You are a top-performing B2B SDR known for replies, not blasts. You read one piece of " +
        "public web content, judge honestly whether the author/company is a real fit for a " +
        "specific offer, and if so write outreach so specific it could not have been sent to " +
        "anyone else. You never invent facts. You would rather say NO than send a generic pitch.";
    private readonly LlmOptions _o = options.Value;

    public async Task<EnrichmentResult?> EnrichAsync(RawItem item, NicheOptions niche, SignalResult signal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_o.ApiKey)) return null;
        var prompt = BuildPrompt(item, niche, signal);
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
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _o.BaseUrl)
            { Content = JsonContent.Create(body) };
            req.Headers.Add("Authorization", $"Bearer {_o.ApiKey}");
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            var content = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return string.IsNullOrWhiteSpace(content) ? null : EnrichmentParser.Parse(content);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException)
        {
            return null;   // rate limit / transient → skip (retried next run), never crash the batch
        }
    }

    // Ported from enrich.py PROMPT_TEMPLATE.
    private static string BuildPrompt(RawItem item, NicheOptions n, SignalResult signal) =>
        $"""
        OFFER WE SELL:
        {n.Offer}

        WHO IS A GOOD FIT:
        (niche described by keywords: {string.Join(", ", n.TargetKeywords)})

        WHO IS NOT A FIT — mark FIT: NO if ANY of these apply, even when keywords match:
        {string.Join("\n", n.Disqualifiers.Select(d => "- " + d))}

        HARVESTED CONTENT (source: {item.Source} · author: {item.Author}):
        \"\"\"
        {item.Text}
        \"\"\"

        Pre-detected signals (keyword-level, may be noisy): {string.Join(", ", signal.Matched)}

        Return EXACTLY these five sections, each on its own line with the label:

        FIT: YES | MAYBE | NO
        SUMMARY: one sentence — who they are and the concrete reason they do or don't fit.
        SUBJECT: a 4-7 word subject line referencing their specific situation (no clickbait, no emojis).
        DRAFT: 3-4 sentences from {n.SenderName} ({n.SenderTitle}). Open with a SPECIFIC detail from the content, connect it to one concrete way the offer helps, end with one low-friction question. No greeting or signature. Banned: "I hope this email finds you well", "reaching out", "game-changer", "circle back", "touch base".
        COMPANY: the real company/org name this is about, or "-" if it cannot be determined. Never guess.

        If FIT is NO, still fill SUBJECT and DRAFT with "-".
        """;
}

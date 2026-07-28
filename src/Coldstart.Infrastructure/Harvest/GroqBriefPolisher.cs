using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Customers;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Harvest;

internal sealed class GroqBriefPolisher(HttpClient http, IOptions<LlmOptions> options) : ICompanyBriefPolisher
{
    private const string System =
        "You tighten a founder's rough notes about their own company into a crisp 2-3 sentence " +
        "brief written in the company's own voice — first-person plural (\"we sell…\", \"we help…\"), " +
        "never third person (\"they\", \"their company\"). It is used as sender context in a cold " +
        "outreach email. Never invent facts, numbers, or claims not present in the input. " +
        "Return ONLY the brief — no preamble, no quotes.";

    private readonly LlmOptions _o = options.Value;

    public async Task<string?> PolishAsync(string rawBrief, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_o.ApiKey) || string.IsNullOrWhiteSpace(rawBrief)) return null;
        var body = new
        {
            model = _o.Model,
            temperature = 0.3,
            messages = new[]
            {
                new { role = "system", content = System },
                new { role = "user", content = rawBrief },
            },
        };
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _o.BaseUrl) { Content = JsonContent.Create(body) };
            req.Headers.Add("Authorization", $"Bearer {_o.ApiKey}");
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (!json.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0) return null;
            var content = choices[0].GetProperty("message").GetProperty("content").GetString();
            return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
            or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        {
            return null;   // best-effort — caller keeps the raw text
        }
    }
}

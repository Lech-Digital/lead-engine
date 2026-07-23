using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Customers;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Harvest;

internal sealed class GroqSearchProfileGenerator(HttpClient http, IOptions<LlmOptions> options) : ISearchProfileGenerator
{
    private const string System =
        "You turn a B2B company's niche and what they sell into a cold-outreach lead-search profile. " +
        "Return EXACTLY four labelled lines and nothing else:\n" +
        "OFFER: one crisp sentence describing what they sell, phrased as the offer to a prospect.\n" +
        "KEYWORDS: 8-12 comma-separated words/phrases that identify a prospect who fits (roles, industry terms).\n" +
        "SIGNALS: 5-8 comma-separated buying signals that mean 'reach out now' (e.g. hiring, expanding, raised).\n" +
        "DISQUALIFIERS: 2-4 comma-separated descriptions of who is NOT a fit. " +
        "Invent nothing beyond the input; be concrete.";

    private readonly LlmOptions _o = options.Value;

    public async Task<SearchProfile?> GenerateAsync(SearchProfileRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_o.ApiKey)) return null;
        var body = new
        {
            model = _o.Model,
            temperature = 0.3,
            messages = new[]
            {
                new { role = "system", content = System },
                new { role = "user", content = $"NICHE: {request.Niche}\nINFO: {request.Info}" },
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
            return string.IsNullOrWhiteSpace(content) ? null : SearchProfileParser.Parse(content);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
            or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        {
            return null;   // best-effort — the invite proceeds without a profile
        }
    }
}

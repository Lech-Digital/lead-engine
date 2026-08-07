using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Customers;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Harvest;

internal sealed class GroqTargetingGenerator(HttpClient http, IOptions<LlmOptions> options) : ITargetingGenerator
{
    private const string System =
        "You turn a B2B company's own description of itself into a structured cold-outreach targeting " +
        "profile. Return ONLY labelled lines, at most one per label, from this exact set:\n" +
        "INDUSTRIES: comma-separated industries/verticals to target.\n" +
        "GEOGRAPHIES: comma-separated countries/regions to target.\n" +
        "ROLES: comma-separated buyer job titles/roles to target.\n" +
        "SIZE_MIN: a single integer, the smallest company size (employees) to target.\n" +
        "SIZE_MAX: a single integer, the largest company size (employees) to target.\n" +
        "TRIGGERS: comma-separated events that signal a good time to reach out (e.g. hiring, funding).\n" +
        "EXCLUSIONS: comma-separated kinds of company to exclude (e.g. competitors, existing customers).\n" +
        "If you cannot confidently infer a label from what you were given, leave that line out entirely — " +
        "do not guess or invent a value. A missing label is far better than a wrong one: a wrong industry " +
        "or geography silently sends the search hunting in the wrong market. Do not include any other text.";

    private readonly LlmOptions _o = options.Value;

    public async Task<TargetingInput?> ProposeAsync(TargetingProposalRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_o.ApiKey)) return null;
        var body = new
        {
            model = _o.Model,
            temperature = 0.3,
            messages = new[]
            {
                new { role = "system", content = System },
                new
                {
                    role = "user",
                    content = $"COMPANY: {request.CompanyName}\nNICHE: {request.Niche}\n" +
                        $"BRIEF: {request.Brief}\nOFFER: {request.Offer}",
                },
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
            return string.IsNullOrWhiteSpace(content) ? null : TargetingProposalParser.Parse(content);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
            or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        {
            return null;   // best-effort — the customer falls back to filling the form themselves
        }
    }
}

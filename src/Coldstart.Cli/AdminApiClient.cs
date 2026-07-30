using System.Text;
using System.Text.Json;

namespace Coldstart.Cli;

internal sealed record ApiResult(int Status, string Body)
{
    public bool Ok => Status is >= 200 and < 300;
}

internal sealed class AdminApiClient(HttpClient http)
{
    public Task<ApiResult> GetAsync(string path, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, path, null, ct);

    public Task<ApiResult> PostAsync(string path, object? body = null, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, path, body, ct);

    public Task<ApiResult> DeleteAsync(string path, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, path, null, ct);

    private async Task<ApiResult> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, ct);
        return new ApiResult((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
    }
}

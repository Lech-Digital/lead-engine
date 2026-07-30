using System.Net;
using System.Text;

namespace Coldstart.Cli.Tests;

// Canned-response HttpMessageHandler: the CLI is tested end-to-end minus the network.
internal sealed class StubHandler(Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> respond)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        var (status, body) = respond(request);
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }

    internal static AdminApiClient Client(Func<HttpRequestMessage, (HttpStatusCode, string)> respond,
        out StubHandler handler)
    {
        handler = new StubHandler(respond);
        return new AdminApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://test.local") });
    }
}

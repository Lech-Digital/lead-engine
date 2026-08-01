using System.Net;
using System.Text;
using Coldstart.Application.Harvest;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class WebSearchSourceTests
{
    private sealed class CapturingHandler(string body) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public List<string?> Keys { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Bodies.Add(r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct));
            Keys.Add(r.Headers.TryGetValues("X-API-KEY", out var v) ? v.First() : null);
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private const string Json = """
    {"news":[
      {"title":"Fietsly raises €2M seed to fix bike logistics","link":"https://news.example/fietsly",
       "snippet":"The Amsterdam startup hires its first sales rep.","date":"2 days ago","source":"Silicon Canals"},
      {"title":"","link":"https://news.example/broken","snippet":"no title","date":"","source":"X"}
    ]}
    """;

    private static NicheOptions Niche(string apiKey = "serper-key") => new()
    {
        TargetKeywords = ["first sales hire", "SDR"],
        WebSearch = new WebSearchOptions { ApiKey = apiKey, MaxQueries = 2, Gl = "nl" },
    };

    [Fact]
    public async Task FetchAsync_QueriesPerRunKeyword_AndMapsNewsToRawItems()
    {
        // Arrange
        var handler = new CapturingHandler(Json);
        var sut = new WebSearchSource(new HttpClient(handler), Niche());

        // Act
        var items = await sut.FetchAsync(Niche(), CancellationToken.None);

        // Assert — one query per keyword, key on every request, titleless hits dropped
        handler.Bodies.Should().HaveCount(2);
        handler.Bodies[0].Should().Contain("first sales hire");
        handler.Bodies[1].Should().Contain("SDR");
        handler.Keys.Should().AllBe("serper-key");
        items.Should().HaveCount(2);   // the good hit, once per query
        items[0].Title.Should().Contain("Fietsly");
        items[0].Text.Should().StartWith(WebSearchSource.Sentinel).And.Contain("Silicon Canals");
        items[0].Url.Should().Be("https://news.example/fietsly");
    }

    [Fact]
    public void Enabled_WithoutApiKey_IsFalse()
    {
        // Arrange
        var sut = new WebSearchSource(new HttpClient(new CapturingHandler(Json)), Niche(apiKey: ""));

        // Act & Assert — soft-degrade: no key, no source, no crash
        sut.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task FetchAsync_WhenApiErrors_ReturnsEmptyInsteadOfThrowing()
    {
        // Arrange — quota exceeded / bad key
        var handler = new ErrorHandler();
        var sut = new WebSearchSource(new HttpClient(handler), Niche());

        // Act
        var items = await sut.FetchAsync(Niche(), CancellationToken.None);

        // Assert
        items.Should().BeEmpty();
    }

    private sealed class ErrorHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
    }
}

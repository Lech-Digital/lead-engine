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
        public List<string> Urls { get; } = [];
        public List<string?> Tokens { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Urls.Add(r.RequestUri!.ToString());
            Tokens.Add(r.Headers.TryGetValues("X-Subscription-Token", out var v) ? v.First() : null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private const string Json = """
    {"web":{"results":[
      {"title":"Fietsly raises €2M seed to fix bike logistics","url":"https://news.example/fietsly",
       "description":"The Amsterdam startup hires its first sales rep.","age":"2 days ago","profile":{"name":"Silicon Canals"}},
      {"title":"","url":"https://news.example/broken","description":"no title"}
    ]}}
    """;

    private static NicheOptions Niche(string apiKey = "brave-key") => new()
    {
        TargetKeywords = ["first sales hire", "SDR"],
        WebSearch = new WebSearchOptions { ApiKey = apiKey, MaxQueries = 2, Country = "nl" },
    };

    [Fact]
    public async Task FetchAsync_QueriesPerRunKeyword_AndMapsHitsToRawItems()
    {
        // Arrange
        var handler = new CapturingHandler(Json);
        var sut = new WebSearchSource(new HttpClient(handler), Niche());

        // Act
        var items = await sut.FetchAsync(Niche(), CancellationToken.None);

        // Assert — one query per keyword with freshness + token on every request; titleless hits dropped
        handler.Urls.Should().HaveCount(2);
        handler.Urls[0].Should().Contain("first sales hire").And.Contain("freshness=pw").And.Contain("country=nl");
        handler.Urls[1].Should().Contain("q=SDR");
        handler.Tokens.Should().AllBe("brave-key");
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

        // Act & Assert — soft-degrade: no key means off, never a crash
        sut.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task FetchAsync_WhenApiErrors_ReturnsEmptyInsteadOfThrowing()
    {
        // Arrange — monthly quota exhausted
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

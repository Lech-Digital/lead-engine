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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Urls.Add(r.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private const string Json = """
    {"items":[
      {"title":"Fietsly raises €2M seed to fix bike logistics","link":"https://news.example/fietsly",
       "snippet":"The Amsterdam startup hires its first sales rep.","displayLink":"siliconcanals.com"},
      {"title":"","link":"https://news.example/broken","snippet":"no title","displayLink":"x.com"}
    ]}
    """;

    private static NicheOptions Niche(string apiKey = "g-key", string cx = "cx-1") => new()
    {
        TargetKeywords = ["first sales hire", "SDR"],
        WebSearch = new WebSearchOptions { ApiKey = apiKey, Cx = cx, MaxQueries = 2, Gl = "nl" },
    };

    [Fact]
    public async Task FetchAsync_QueriesPerRunKeyword_AndMapsHitsToRawItems()
    {
        // Arrange
        var handler = new CapturingHandler(Json);
        var sut = new WebSearchSource(new HttpClient(handler), Niche());

        // Act
        var items = await sut.FetchAsync(Niche(), CancellationToken.None);

        // Assert — one query per keyword with freshness + key + cx; titleless hits dropped
        handler.Urls.Should().HaveCount(2);
        handler.Urls[0].Should().Contain("first sales hire").And.Contain("dateRestrict=w1")
            .And.Contain("key=g-key").And.Contain("cx=cx-1");
        handler.Urls[1].Should().Contain("q=SDR");
        items.Should().HaveCount(2);   // the good hit, once per query
        items[0].Title.Should().Contain("Fietsly");
        items[0].Text.Should().StartWith(WebSearchSource.Sentinel).And.Contain("siliconcanals.com");
        items[0].Url.Should().Be("https://news.example/fietsly");
    }

    [Theory]
    [InlineData("", "cx-1")]
    [InlineData("g-key", "")]
    public void Enabled_WithoutKeyOrCx_IsFalse(string key, string cx)
    {
        // Arrange
        var sut = new WebSearchSource(new HttpClient(new CapturingHandler(Json)), Niche(key, cx));

        // Act & Assert — soft-degrade: incomplete config means off, never a crash
        sut.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task FetchAsync_WhenApiErrors_ReturnsEmptyInsteadOfThrowing()
    {
        // Arrange — daily quota exhausted
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

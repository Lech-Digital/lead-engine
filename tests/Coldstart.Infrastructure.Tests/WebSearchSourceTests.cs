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
    {"organic_results":[
      {"title":"Fietsly raises €2M seed to fix bike logistics","link":"https://news.example/fietsly",
       "snippet":"The Amsterdam startup hires its first sales rep.","displayed_link":"news.example/fietsly","source":"Silicon Canals","rank":1},
      {"title":"","link":"https://news.example/broken","snippet":"no title","rank":2}
    ]}
    """;

    // Day-of-year 1 (Jan 1) → rotation offset 1 % keyword-count.
    private static readonly Func<DateTimeOffset> Jan1 = () => new DateTimeOffset(2026, 1, 1, 4, 0, 0, TimeSpan.Zero);

    private static NicheOptions Niche(string apiKey = "sd-key", int maxQueries = 2) => new()
    {
        TargetKeywords = ["first sales hire", "SDR"],
        WebSearch = new WebSearchOptions { ApiKey = apiKey, MaxQueries = maxQueries, Country = "nl" },
    };

    [Fact]
    public async Task FetchAsync_QueriesScrapingdogPerKeyword_AndMapsOrganicResultsToRawItems()
    {
        // Arrange
        var handler = new CapturingHandler(Json);
        var sut = new WebSearchSource(new HttpClient(handler), Niche(), Jan1);

        // Act
        var items = await sut.FetchAsync(Niche(), CancellationToken.None);

        // Assert — one query per keyword with api_key + week-freshness tbs; titleless hits dropped
        handler.Urls.Should().HaveCount(2);
        handler.Urls.Should().AllSatisfy(u => u.Should()
            .Contain("api.scrapingdog.com/search")
            .And.Contain("api_key=sd-key")
            .And.Contain("tbs=qdr%3Aw")
            .And.Contain("country=nl")
            .And.Contain("results=10"));
        items.Should().HaveCount(2);   // the good hit, once per query
        items[0].Title.Should().Contain("Fietsly");
        items[0].Text.Should().StartWith(WebSearchSource.Sentinel).And.Contain("Silicon Canals");
        items[0].Url.Should().Be("https://news.example/fietsly");
    }

    [Fact]
    public async Task FetchAsync_RotatesKeywordsByDay_SoOneQueryPerNightCoversAllKeywordsOverTime()
    {
        // Arrange — 2 keywords, budget of 1 query: Jan 1 (day 1 % 2 = 1) must pick the SECOND keyword
        var handler = new CapturingHandler(Json);
        var sut = new WebSearchSource(new HttpClient(handler), Niche(maxQueries: 1), Jan1);

        // Act
        await sut.FetchAsync(Niche(maxQueries: 1), CancellationToken.None);

        // Assert
        handler.Urls.Should().HaveCount(1);
        handler.Urls[0].Should().Contain("query=SDR");
    }

    [Fact]
    public async Task FetchAsync_RotationWrapsAround_WhenWindowPassesEndOfList()
    {
        // Arrange — offset 1 with a 2-keyword window: SDR first, then wrap to the first keyword
        var handler = new CapturingHandler(Json);
        var sut = new WebSearchSource(new HttpClient(handler), Niche(), Jan1);

        // Act
        await sut.FetchAsync(Niche(), CancellationToken.None);

        // Assert
        // Uri.ToString() shows the escaped %20 back as a space.
        handler.Urls[0].Should().Contain("query=SDR");
        handler.Urls[1].Should().Contain("query=first sales hire");
    }

    [Fact]
    public void Enabled_WithoutApiKey_IsFalse()
    {
        // Arrange
        var sut = new WebSearchSource(new HttpClient(new CapturingHandler(Json)), Niche(apiKey: ""), Jan1);

        // Act & Assert — soft-degrade: no key means off, never a crash
        sut.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task FetchAsync_WhenApiErrors_ReturnsEmptyInsteadOfThrowing()
    {
        // Arrange — monthly credit quota exhausted
        var handler = new ErrorHandler();
        var sut = new WebSearchSource(new HttpClient(handler), Niche(), Jan1);

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

using System.Net;
using System.Text;
using Coldstart.Application.Harvest;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class HackerNewsSourceTests
{
    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    [Fact]
    public async Task FetchAsync_MapsHitsToRawItems_StrippingHtml()
    {
        // Arrange
        const string json = """
        {"hits":[{"objectID":"42","comment_text":"We are <b>hiring</b> an SDR &amp; more",
          "story_title":"Ask HN: Who is hiring?","author":"jane","created_at":"2026-07-01T00:00:00Z"}]}
        """;
        var http = new HttpClient(new StubHandler(json));
        var niche = new NicheOptions { HackerNews = new() { Queries = ["hiring"], ScoreWeight = 0.7 } };
        var sut = new HackerNewsSource(http, niche);

        // Act
        var items = await sut.FetchAsync(CancellationToken.None);

        // Assert
        items.Should().ContainSingle();
        items[0].Source.Should().Be("hackernews");
        items[0].ExternalId.Should().Be("42");
        items[0].Text.Should().Be("We are hiring an SDR & more");   // tags stripped, entities decoded
        items[0].Url.Should().Be("https://news.ycombinator.com/item?id=42");
        sut.ScoreWeight.Should().Be(0.7);
    }

    [Fact]
    public async Task FetchAsync_WhenResponseBodyIsNotAnObject_ReturnsEmptyWithoutThrowing()
    {
        // Arrange
        const string json = "null";
        var http = new HttpClient(new StubHandler(json));
        var niche = new NicheOptions { HackerNews = new() { Queries = ["hiring"], ScoreWeight = 0.7 } };
        var sut = new HackerNewsSource(http, niche);

        // Act
        Func<Task> act = async () => await sut.FetchAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        var items = await sut.FetchAsync(CancellationToken.None);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_SkipsHitsWithNoText()
    {
        // Arrange
        const string json = """
        {"hits":[{"objectID":"9","author":"x","created_at":"2026-07-01T00:00:00Z"}]}
        """;
        var http = new HttpClient(new StubHandler(json));
        var niche = new NicheOptions { HackerNews = new() { Queries = ["hiring"], ScoreWeight = 0.7 } };
        var sut = new HackerNewsSource(http, niche);

        // Act
        var items = await sut.FetchAsync(CancellationToken.None);

        // Assert
        items.Should().BeEmpty();
    }
}

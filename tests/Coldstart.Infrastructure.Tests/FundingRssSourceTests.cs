using System.Net;
using System.Text;
using Coldstart.Application.Harvest;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class FundingRssSourceTests
{
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/xml") });
    }

    private const string Rss = """
    <rss><channel>
      <item><title>Acme raises $12M Series A</title><link>https://x/acme</link>
        <description>Acme, a B2B startup, raised a round led by VC.</description>
        <guid>acme-1</guid><pubDate>Wed, 01 Jul 2026 10:00:00 GMT</pubDate></item>
      <item><title>Opinion: why VC is broken</title><link>https://x/op</link>
        <description>A columnist shares views on the market.</description>
        <guid>op-1</guid><pubDate>Wed, 01 Jul 2026 10:00:00 GMT</pubDate></item>
    </channel></rss>
    """;

    [Fact]
    public async Task FetchAsync_KeepsFundingItems_DropsEditorial_AndPrependsSentinel()
    {
        // Arrange
        var http = new HttpClient(new StubHandler(Rss));
        var niche = new NicheOptions { Funding = new() { Feeds = ["https://x/feed"], RequireFundingTerms = true, LookbackDays = 100000 } };
        var sut = new FundingRssSource(http, niche);

        // Act
        var items = await sut.FetchAsync(CancellationToken.None);

        // Assert
        items.Should().ContainSingle();
        items[0].Title.Should().Contain("Acme raises");
        items[0].Text.Should().StartWith("FUNDING SIGNAL");
        items[0].Source.Should().Be("funding");
        sut.BaseScore.Should().Be(0.5);
    }

    [Fact]
    public async Task FetchAsync_WhenFeedBodyIsMalformedXml_ReturnsEmptyWithoutThrowing()
    {
        // Arrange
        const string malformed = "<rss><channel><item><title>Unclosed";
        var http = new HttpClient(new StubHandler(malformed));
        var niche = new NicheOptions { Funding = new() { Feeds = ["https://x/feed"] } };
        var sut = new FundingRssSource(http, niche);

        // Act
        Func<Task> act = async () => await sut.FetchAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        var items = await sut.FetchAsync(CancellationToken.None);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_DropsItemsOlderThanLookback_ButKeepsUndatedItems()
    {
        // Arrange
        const string rss = """
        <rss><channel>
          <item><title>StaleCo raises $5M seed</title><link>https://x/stale</link>
            <description>StaleCo raised a round led by an investor.</description>
            <guid>stale-1</guid><pubDate>Wed, 01 Jan 2020 10:00:00 GMT</pubDate></item>
          <item><title>FreshCo raises $8M seed</title><link>https://x/fresh</link>
            <description>FreshCo raised a round led by an investor. No date given.</description>
            <guid>fresh-1</guid></item>
        </channel></rss>
        """;
        var http = new HttpClient(new StubHandler(rss));
        var niche = new NicheOptions { Funding = new() { Feeds = ["https://x/feed"], RequireFundingTerms = true, LookbackDays = 30 } };
        var sut = new FundingRssSource(http, niche);

        // Act
        var items = await sut.FetchAsync(CancellationToken.None);

        // Assert
        items.Should().ContainSingle();
        items[0].Title.Should().Contain("FreshCo raises");
    }

    [Fact]
    public async Task FetchAsync_ParsesAtomFeed_UsingLinkHrefAndPublished()
    {
        // Arrange
        const string atom = """
        <feed xmlns="http://www.w3.org/2005/Atom">
          <entry>
            <title>Beta lands $20M Series B</title>
            <link href="https://x/beta"/>
            <summary>Beta landed a big round led by a top-tier fund.</summary>
            <id>beta-1</id>
            <published>2026-07-01T09:00:00Z</published>
          </entry>
        </feed>
        """;
        var http = new HttpClient(new StubHandler(atom));
        var niche = new NicheOptions { Funding = new() { Feeds = ["https://x/feed"], RequireFundingTerms = true, LookbackDays = 100000 } };
        var sut = new FundingRssSource(http, niche);

        // Act
        var items = await sut.FetchAsync(CancellationToken.None);

        // Assert
        items.Should().ContainSingle();
        items[0].Url.Should().Be("https://x/beta");
        items[0].Text.Should().StartWith("FUNDING SIGNAL");
    }
}

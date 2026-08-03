using System.Net;
using System.Text;
using Coldstart.Application.Harvest;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class LangSearchSourceTests
{
    private sealed class CapturingHandler(string body) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        public List<string?> Auth { get; } = [];
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Urls.Add(r.RequestUri!.ToString());
            Auth.Add(r.Headers.Authorization?.ToString());
            Bodies.Add(r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private const string Json = """
    {"code":200,"data":{"webPages":{"value":[
      {"name":"Dutch B2B SaaS firm Partnify secures €1.8M","url":"https://news.example/partnify",
       "displayUrl":"https://news.example/partnify","snippet":"netherlands - based b2b saas firm part ni fy ...","siteName":"Vestbee"},
      {"name":"","url":"https://news.example/broken","snippet":"no name"}
    ]}}}
    """;

    // Day-of-year 1 (Jan 1) → rotation offset 1 % keyword-count.
    private static readonly Func<DateTimeOffset> Jan1 = () => new DateTimeOffset(2026, 1, 1, 4, 0, 0, TimeSpan.Zero);

    private static NicheOptions Niche(string apiKey = "sk-lang", int maxQueries = 2) => new()
    {
        TargetKeywords = ["first sales hire", "SDR"],
        LangSearch = new LangSearchOptions { ApiKey = apiKey, MaxQueries = maxQueries },
    };

    [Fact]
    public async Task FetchAsync_PostsPerKeywordWithBearerKey_AndMapsWebPagesToRawItems()
    {
        // Arrange
        var handler = new CapturingHandler(Json);
        var sut = new LangSearchSource(new HttpClient(handler), Niche(), Jan1);

        // Act
        var items = await sut.FetchAsync(Niche(), CancellationToken.None);

        // Assert — one POST per keyword, Bearer auth, count + freshness in the body; nameless hits dropped
        handler.Urls.Should().HaveCount(2).And.AllSatisfy(u => u.Should().Contain("api.langsearch.com/v1/web-search"));
        handler.Auth.Should().AllBe("Bearer sk-lang");
        handler.Bodies.Should().AllSatisfy(b => b.Should().Contain("\"count\":10").And.Contain("\"freshness\":\"oneWeek\""));
        items.Should().HaveCount(2);   // the good hit, once per query
        items[0].Title.Should().Contain("Partnify");
        items[0].Url.Should().Be("https://news.example/partnify");
        items[0].Author.Should().Be("Vestbee");
    }

    [Fact]
    public async Task FetchAsync_SentinelNeverClaimsFreshness_BecauseLangSearchDatesAreUnreliable()
    {
        // Arrange
        var handler = new CapturingHandler(Json);
        var sut = new LangSearchSource(new HttpClient(handler), Niche(), Jan1);

        // Act
        var items = await sut.FetchAsync(Niche(), CancellationToken.None);

        // Assert — the LLM must be told the date is unknown, or it congratulates months-old news
        items[0].Text.Should().StartWith(LangSearchSource.Sentinel);
        LangSearchSource.Sentinel.Should().Contain("UNKNOWN").And.NotContain("this week");
    }

    [Fact]
    public async Task FetchAsync_RotatesKeywordsByDay_LikeTheMeteredSource()
    {
        // Arrange — 2 keywords, budget of 1 query: Jan 1 (day 1 % 2 = 1) must pick the SECOND keyword
        var handler = new CapturingHandler(Json);
        var sut = new LangSearchSource(new HttpClient(handler), Niche(maxQueries: 1), Jan1);

        // Act
        await sut.FetchAsync(Niche(maxQueries: 1), CancellationToken.None);

        // Assert
        handler.Bodies.Should().HaveCount(1);
        handler.Bodies[0].Should().Contain("\"query\":\"SDR\"");
    }

    [Fact]
    public void Enabled_WithoutApiKey_IsFalse()
    {
        // Arrange
        var sut = new LangSearchSource(new HttpClient(new CapturingHandler(Json)), Niche(apiKey: ""), Jan1);

        // Act & Assert — soft-degrade: no key means off, never a crash
        sut.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task FetchAsync_WhenApiErrors_ReturnsEmptyInsteadOfThrowing()
    {
        // Arrange
        var handler = new ErrorHandler();
        var sut = new LangSearchSource(new HttpClient(handler), Niche(), Jan1);

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

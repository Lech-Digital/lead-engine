using System.Net;
using System.Text;
using Coldstart.Application.Harvest;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class AdzunaSourceTests
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
    {"results":[
      {"id":"9001","title":"First Sales Hire (SDR)","description":"Scale-up seeks its first SDR to build outbound.",
       "redirect_url":"https://adzuna.example/j/9001","created":"2026-07-30T08:00:00Z",
       "company":{"display_name":"Fietsly B.V."},"location":{"display_name":"Amsterdam"}},
      {"id":"9002","title":"Head of Growth","description":"Own the pipeline.",
       "redirect_url":"https://adzuna.example/j/9002","created":"2026-07-30T09:00:00Z",
       "company":{},"location":{"display_name":"Utrecht"}}
    ]}
    """;

    private static NicheOptions Niche(string appId = "id1", string appKey = "key1") => new()
    {
        TargetKeywords = ["first sales hire", "SDR"],
        Adzuna = new AdzunaOptions { AppId = appId, AppKey = appKey, Country = "nl", MaxQueries = 2 },
    };

    [Fact]
    public async Task FetchAsync_QueriesPerRunKeywords_AndMapsJobsToRawItems()
    {
        // Arrange
        var handler = new CapturingHandler(Json);
        var sut = new AdzunaSource(new HttpClient(handler), Niche());

        // Act
        var items = await sut.FetchAsync(Niche(), CancellationToken.None);

        // Assert — one URL per keyword, against the NL endpoint, with credentials
        handler.Urls.Should().HaveCount(2);
        handler.Urls[0].Should().Contain("/jobs/nl/search/1").And.Contain("what=first sales hire").And.Contain("app_id=id1");
        // The company-less job is dropped; the good one carries the sentinel + company as author
        items.Should().HaveCount(2); // 1 valid job per query x 2 queries
        items[0].Author.Should().Be("Fietsly B.V.");
        items[0].Text.Should().StartWith(AdzunaSource.Sentinel).And.Contain("Fietsly B.V. is hiring: First Sales Hire");
        items[0].Url.Should().Be("https://adzuna.example/j/9001");
    }

    [Fact]
    public void Enabled_RequiresCredentials()
    {
        // Arrange / Act / Assert — soft-degrade without a key
        new AdzunaSource(new HttpClient(new CapturingHandler(Json)), Niche(appId: "")).Enabled.Should().BeFalse();
        new AdzunaSource(new HttpClient(new CapturingHandler(Json)), Niche()).Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task FetchAsync_WhenTheApiFails_ReturnsEmptyInsteadOfThrowing()
    {
        // Arrange
        var sut = new AdzunaSource(new HttpClient(new FailingHandler()), Niche());

        // Act
        var items = await sut.FetchAsync(Niche(), CancellationToken.None);

        // Assert
        items.Should().BeEmpty();
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            throw new HttpRequestException("api down");
    }
}

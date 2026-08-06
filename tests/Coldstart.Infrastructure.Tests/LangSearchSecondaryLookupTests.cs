using System.Net;
using System.Text;
using Coldstart.Application.Harvest;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class LangSearchSecondaryLookupTests
{
    private sealed class CapturingHandler(string body) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public List<string?> Auth { get; } = [];
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            Auth.Add(r.Headers.Authorization?.ToString());
            Bodies.Add(r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ErrorHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private const string Json = """
    {"code":200,"data":{"webPages":{"value":[
      {"name":"Ellis AI blog","url":"https://news.example/ellis","snippet":"snippet text from the fake response"}
    ]}}}
    """;

    private static NicheOptions Niche(bool enabled = true, string apiKey = "sk-lang") => new()
    {
        LangSearch = new LangSearchOptions { ApiKey = apiKey, SecondaryContextEnabled = enabled },
    };

    [Fact]
    public async Task LookupAsync_HitFound_ReturnsSnippetWithHonestyPreamble()
    {
        // Arrange — handler returns one webPages hit for "Ellis AI announcement OR blog OR interview"
        var handler = new CapturingHandler(Json);
        var sut = new LangSearchSecondaryLookup(new HttpClient(handler), Niche());

        // Act
        var context = await sut.LookupAsync("Ellis AI", CancellationToken.None);

        // Assert
        context.Should().NotBeNull();
        context.Should().Contain("publication date unknown");   // honesty preamble
        context.Should().Contain("snippet text from the fake response");
        handler.Auth.Should().AllBe("Bearer sk-lang");
    }

    [Fact]
    public async Task LookupAsync_ApiFails_ReturnsNull()
    {
        // Arrange — handler returns 500
        var handler = new ErrorHandler();
        var sut = new LangSearchSecondaryLookup(new HttpClient(handler), Niche());

        // Act
        var context = await sut.LookupAsync("Ellis AI", CancellationToken.None);

        // Assert
        context.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_ThrowsUnderlyingException_ReturnsNull()
    {
        // Arrange — the http client throws instead of returning an error response
        var handler = new ThrowingHandler();
        var sut = new LangSearchSecondaryLookup(new HttpClient(handler), Niche());

        // Act
        var context = await sut.LookupAsync("Ellis AI", CancellationToken.None);

        // Assert — best-effort by contract: never throws, never returns anything but null
        context.Should().BeNull();
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            throw new HttpRequestException("boom");
    }

    [Fact]
    public async Task LookupAsync_Disabled_ReturnsNullWithoutAnyHttpCall()
    {
        // Arrange — SecondaryContextEnabled = false
        var handler = new CapturingHandler(Json);
        var sut = new LangSearchSecondaryLookup(new HttpClient(handler), Niche(enabled: false));

        // Act
        var context = await sut.LookupAsync("Ellis AI", CancellationToken.None);

        // Assert
        context.Should().BeNull();
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task LookupAsync_NoApiKey_ReturnsNullWithoutAnyHttpCall()
    {
        // Arrange — ApiKey = ""
        var handler = new CapturingHandler(Json);
        var sut = new LangSearchSecondaryLookup(new HttpClient(handler), Niche(apiKey: ""));

        // Act
        var context = await sut.LookupAsync("Ellis AI", CancellationToken.None);

        // Assert
        context.Should().BeNull();
        handler.Calls.Should().Be(0);
    }

    // Every hit is about something else entirely — the key appears in neither name nor snippet.
    private const string IrrelevantJson = """
    {"code":200,"data":{"webPages":{"value":[
      {"name":"Weekly funding roundup","url":"https://news.example/a","snippet":"Globex closed a Series B this week."},
      {"name":"Best CRMs of 2026","url":"https://news.example/b","snippet":"Our pick of tools for small sales teams."}
    ]}}}
    """;

    // First hit is unrelated, second one actually names the company.
    private const string LaterHitJson = """
    {"code":200,"data":{"webPages":{"value":[
      {"name":"Weekly funding roundup","url":"https://news.example/a","snippet":"Globex closed a Series B this week."},
      {"name":"Interview: building in public","url":"https://news.example/b","snippet":"The founders of ellis ai on their first year."}
    ]}}}
    """;

    [Fact]
    public void AddInfrastructure_SecondaryContextClient_HasABoundedTimeout()
    {
        // Arrange — the real composition root registration
        var services = new ServiceCollection();
        services.AddInfrastructure("Host=localhost;Database=x;Username=x;Password=x");
        var provider = services.BuildServiceProvider();

        // Act
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var secondary = factory.CreateClient(DependencyInjection.SecondaryContextClient);
        var shared = factory.CreateClient();

        // Assert — 10s for the per-lead lookup; the shared default client keeps its own timeout
        secondary.Timeout.Should().Be(TimeSpan.FromSeconds(10));
        shared.Timeout.Should().Be(TimeSpan.FromSeconds(100));
    }

    [Fact]
    public async Task LookupAsync_HitDoesNotMentionTheCompany_ReturnsNull()
    {
        // Arrange — two hits with snippets, neither about "Ellis AI"
        var handler = new CapturingHandler(IrrelevantJson);
        var sut = new LangSearchSecondaryLookup(new HttpClient(handler), Niche());

        // Act
        var context = await sut.LookupAsync("Ellis AI", CancellationToken.None);

        // Assert — an off-topic snippet is worse than none: it would be handed to the model as evidence
        context.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_FirstHitIrrelevantSecondMentionsCompany_ReturnsTheSecond()
    {
        // Arrange — scanning must continue past a non-matching hit; the match is case-insensitive
        var handler = new CapturingHandler(LaterHitJson);
        var sut = new LangSearchSecondaryLookup(new HttpClient(handler), Niche());

        // Act
        var context = await sut.LookupAsync("Ellis AI", CancellationToken.None);

        // Assert
        context.Should().NotBeNull();
        context.Should().Contain("The founders of ellis ai on their first year.");
        context.Should().NotContain("Globex");
    }

    [Fact]
    public async Task LookupAsync_WhenTheHttpClientTimesOut_ReturnsNullWithoutThrowing()
    {
        // Arrange — a hung endpoint: the client's own timeout fires (TaskCanceledException)
        var http = new HttpClient(new HangingHandler()) { Timeout = TimeSpan.FromMilliseconds(50) };
        var sut = new LangSearchSecondaryLookup(http, Niche());

        // Act
        var context = await sut.LookupAsync("Ellis AI", CancellationToken.None);

        // Assert — best-effort: the nightly loop proceeds with a null secondary context
        context.Should().BeNull();
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task LookupAsync_BlankCompany_ReturnsNullWithoutAnyHttpCall()
    {
        // Arrange — company is whitespace only
        var handler = new CapturingHandler(Json);
        var sut = new LangSearchSecondaryLookup(new HttpClient(handler), Niche());

        // Act
        var context = await sut.LookupAsync("   ", CancellationToken.None);

        // Assert
        context.Should().BeNull();
        handler.Calls.Should().Be(0);
    }
}

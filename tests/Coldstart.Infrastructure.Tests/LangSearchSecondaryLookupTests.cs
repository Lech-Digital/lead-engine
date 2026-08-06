using System.Net;
using System.Text;
using Coldstart.Application.Harvest;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
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

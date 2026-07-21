using System.Net;
using System.Text;
using Coldstart.Application.Harvest;
using Coldstart.Domain;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class GroqLeadEnricherTests
{
    private const string ValidBody =
        """{"choices":[{"message":{"content":"FIT: YES\nSUMMARY: s\nSUBJECT: hi\nDRAFT: body\nCOMPANY: Acme"}}]}""";

    // Returns each response factory in order; clamps to the last one for any further calls.
    private sealed class SequenceHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var factory = responses[Math.Min(Calls, responses.Length - 1)];
            Calls++;
            return Task.FromResult(factory());
        }
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage TooMany(int? retryAfterSeconds = null)
    {
        var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (retryAfterSeconds is { } s) resp.Headers.RetryAfter = new(TimeSpan.FromSeconds(s));
        return resp;
    }

    private static IOptions<LlmOptions> Opts() => Options.Create(new LlmOptions { ApiKey = "test-key" });
    private static readonly RawItem Item = new("hn", "1", "", "hiring outbound SDR", "author", "https://x/1", "");
    private static readonly NicheOptions Niche = new() { TargetKeywords = ["outbound"] };
    private static readonly SignalResult Signal = new(0.7, ["outbound"]);

    [Fact]
    public async Task EnrichAsync_WhenRateLimitedThenOk_RetriesAndReturnsResult()
    {
        // Arrange
        var handler = new SequenceHandler(() => TooMany(), () => Ok(ValidBody));
        var delays = new List<TimeSpan>();
        var sut = new GroqLeadEnricher(new HttpClient(handler), Opts(),
            (d, _) => { delays.Add(d); return Task.CompletedTask; });

        // Act
        var result = await sut.EnrichAsync(Item, Niche, Signal, CancellationToken.None);

        // Assert — the 429 was retried, the second (200) response parsed successfully
        result.Should().NotBeNull();
        result!.Fit.Should().Be(Fit.Yes);
        handler.Calls.Should().Be(2);
        delays.Should().ContainSingle();
    }

    [Fact]
    public async Task EnrichAsync_WhenRateLimitedEveryTime_GivesUpAfterMaxRetries_ReturnsNull()
    {
        // Arrange
        var handler = new SequenceHandler(() => TooMany());
        var delays = new List<TimeSpan>();
        var sut = new GroqLeadEnricher(new HttpClient(handler), Opts(),
            (d, _) => { delays.Add(d); return Task.CompletedTask; });

        // Act
        var result = await sut.EnrichAsync(Item, Niche, Signal, CancellationToken.None);

        // Assert — bounded retries: 1 initial + 3 retries = 4 calls, 3 backoffs, then give up
        result.Should().BeNull();
        handler.Calls.Should().Be(4);
        delays.Should().HaveCount(3);
    }

    [Fact]
    public async Task EnrichAsync_WhenCalledBackToBack_PacesRequestsWithConfiguredDelay()
    {
        // Arrange
        var handler = new SequenceHandler(() => Ok(ValidBody));
        var delays = new List<TimeSpan>();
        var opts = Options.Create(new LlmOptions { ApiKey = "test-key", RequestDelaySeconds = 2 });
        var sut = new GroqLeadEnricher(new HttpClient(handler), opts,
            (d, _) => { delays.Add(d); return Task.CompletedTask; });

        // Act
        await sut.EnrichAsync(Item, Niche, Signal, CancellationToken.None);
        await sut.EnrichAsync(Item, Niche, Signal, CancellationToken.None);
        await sut.EnrichAsync(Item, Niche, Signal, CancellationToken.None);

        // Assert — no delay before the first request, the pacing delay before each subsequent one
        handler.Calls.Should().Be(3);
        delays.Should().HaveCount(2);
        delays.Should().AllBeEquivalentTo(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task EnrichAsync_WhenPacingDisabled_DoesNotDelay()
    {
        // Arrange
        var handler = new SequenceHandler(() => Ok(ValidBody));
        var delays = new List<TimeSpan>();
        var opts = Options.Create(new LlmOptions { ApiKey = "test-key", RequestDelaySeconds = 0 });
        var sut = new GroqLeadEnricher(new HttpClient(handler), opts,
            (d, _) => { delays.Add(d); return Task.CompletedTask; });

        // Act
        await sut.EnrichAsync(Item, Niche, Signal, CancellationToken.None);
        await sut.EnrichAsync(Item, Niche, Signal, CancellationToken.None);

        // Assert
        handler.Calls.Should().Be(2);
        delays.Should().BeEmpty();
    }

    [Fact]
    public async Task EnrichAsync_WhenRetryAfterHeaderPresent_HonorsItForTheBackoff()
    {
        // Arrange
        var handler = new SequenceHandler(() => TooMany(retryAfterSeconds: 2), () => Ok(ValidBody));
        var delays = new List<TimeSpan>();
        var sut = new GroqLeadEnricher(new HttpClient(handler), Opts(),
            (d, _) => { delays.Add(d); return Task.CompletedTask; });

        // Act
        await sut.EnrichAsync(Item, Niche, Signal, CancellationToken.None);

        // Assert — the server-provided Retry-After drives the wait, not the exponential fallback
        delays.Should().ContainSingle().Which.Should().Be(TimeSpan.FromSeconds(2));
    }
}

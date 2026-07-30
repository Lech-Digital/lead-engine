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

    // Captures the outgoing request body so tests can assert on the exact prompt sent.
    private sealed class CapturingHandler(string body) : HttpMessageHandler
    {
        public string? SentBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            SentBody = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
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
        var result = await sut.EnrichAsync(Item, Niche, Signal, SenderContext.None, CancellationToken.None);

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
        var result = await sut.EnrichAsync(Item, Niche, Signal, SenderContext.None, CancellationToken.None);

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
        await sut.EnrichAsync(Item, Niche, Signal, SenderContext.None, CancellationToken.None);
        await sut.EnrichAsync(Item, Niche, Signal, SenderContext.None, CancellationToken.None);
        await sut.EnrichAsync(Item, Niche, Signal, SenderContext.None, CancellationToken.None);

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
        await sut.EnrichAsync(Item, Niche, Signal, SenderContext.None, CancellationToken.None);
        await sut.EnrichAsync(Item, Niche, Signal, SenderContext.None, CancellationToken.None);

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
        await sut.EnrichAsync(Item, Niche, Signal, SenderContext.None, CancellationToken.None);

        // Assert — the server-provided Retry-After drives the wait, not the exponential fallback
        delays.Should().ContainSingle().Which.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task EnrichAsync_WithBrief_IncludesSenderContextInPrompt()
    {
        // Arrange
        var handler = new CapturingHandler(ValidBody);
        var sut = new GroqLeadEnricher(new HttpClient(handler), Opts(), (d, _) => Task.CompletedTask);
        var sender = new SenderContext("Acme helps dentists fill calendars.", MentionSourceTool: false);

        // Act
        await sut.EnrichAsync(Item, Niche, Signal, sender, CancellationToken.None);

        // Assert — the brief is in the prompt; the Coldstart line is NOT (toggle off)
        handler.SentBody.Should().Contain("Acme helps dentists fill calendars.");
        handler.SentBody.Should().NotContain("Coldstart");
    }

    [Fact]
    public async Task EnrichAsync_WithMentionSourceTool_AddsColdstartInstruction()
    {
        // Arrange
        var handler = new CapturingHandler(ValidBody);
        var sut = new GroqLeadEnricher(new HttpClient(handler), Opts(), (d, _) => Task.CompletedTask);
        var sender = new SenderContext(Brief: null, MentionSourceTool: true);

        // Act
        await sut.EnrichAsync(Item, Niche, Signal, sender, CancellationToken.None);

        // Assert — the dogfood line must be an explicit, first-person "found you with the
        // tool I'm offering you" statement, not a vague hint.
        handler.SentBody.Should().Contain("Coldstart");
        handler.SentBody.Should().Contain("first person");
        handler.SentBody.Should().Contain("the very tool this email offers");
    }

    [Fact]
    public async Task EnrichAsync_WithFixedPitch_AsksForHookOnlyAndSkipsSellingInstructions()
    {
        // Arrange — when the sender has hardcoded pitch/CTA blocks, the model writes ONLY
        // the personalized hook; the selling copy is composed in code afterwards.
        var handler = new CapturingHandler(ValidBody);
        var sut = new GroqLeadEnricher(new HttpClient(handler), Opts(), (d, _) => Task.CompletedTask);
        var sender = new SenderContext(Brief: null, MentionSourceTool: true, PitchLine: "I run Coldstart.", CtaLine: "3 free leads: link");

        // Act
        await sut.EnrichAsync(Item, Niche, Signal, sender, CancellationToken.None);

        // Assert — hook-only instructions; no AI-written pitch/CTA/dogfood line
        handler.SentBody.Should().Contain("ONLY the greeting and the personalization");
        handler.SentBody.Should().Contain("appended automatically");
        handler.SentBody.Should().NotContain("the very tool this email offers");
        // Empathy + their-language + honest-bridge requirements
        handler.SentBody.Should().Contain("own vocabulary"); // note: apostrophes JSON-escape to ' in the body
        handler.SentBody.Should().Contain("never claim leads are already found");
        // Direct address: TO them (you/your), compliment-first, never third-person about their company
        handler.SentBody.Should().Contain("Speak TO them, not about them");
        handler.SentBody.Should().Contain("genuine, specific compliment");
    }

    [Fact]
    public async Task EnrichAsync_Always_SubjectFollowsTheirMomentPlusIdeaPattern()
    {
        // Arrange
        var handler = new CapturingHandler(ValidBody);
        var sut = new GroqLeadEnricher(new HttpClient(handler), Opts(), (d, _) => Task.CompletedTask);

        // Act
        await sut.EnrichAsync(Item, Niche, Signal, SenderContext.None, CancellationToken.None);

        // Assert
        handler.SentBody.Should().Contain("an idea for");
    }

    [Fact]
    public async Task EnrichAsync_Always_PromptAsksForGreetingAndParagraphs()
    {
        // Arrange
        var handler = new CapturingHandler(ValidBody);
        var sut = new GroqLeadEnricher(new HttpClient(handler), Opts(), (d, _) => Task.CompletedTask);

        // Act
        await sut.EnrichAsync(Item, Niche, Signal, SenderContext.None, CancellationToken.None);

        // Assert — drafts open with a greeting and are laid out as paragraphs, never one block
        handler.SentBody.Should().Contain("Hi there");
        handler.SentBody.Should().Contain("greeting");
        handler.SentBody.Should().Contain("paragraphs separated by ONE blank line");
        handler.SentBody.Should().Contain("Never guess or invent a name");
        // When the offer contains a free / no-commitment element, the closing question
        // must offer exactly that — it's the lowest-friction ask available.
        handler.SentBody.Should().Contain("free or no-commitment");
    }
}

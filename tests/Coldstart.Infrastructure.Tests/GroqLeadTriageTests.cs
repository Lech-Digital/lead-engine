using System.Net;
using System.Text;
using Coldstart.Application.Harvest;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class GroqLeadTriageTests
{
    private const string ValidBody =
        """{"choices":[{"message":{"content":"VERDICT: FIT\nCOMPANY: Acme BV\nMET: early stage\nFAILED: none"}}]}""";

    // Reused from GroqLeadEnricherTests' fixture: returns each response factory in order,
    // clamping to the last one for any further calls.
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

    private static IOptions<LlmOptions> Opts() => Options.Create(new LlmOptions { ApiKey = "test-key" });
    private static readonly RawItem Item = new("hn", "1", "", "Acme BV raises seed round", "author", "https://x/1", "");
    private const string Criteria = "Industries: SaaS. Geographies: Netherlands.";

    [Fact]
    public async Task JudgeAsync_WellFormedResponse_ReturnsVerdict()
    {
        // Arrange
        var handler = new SequenceHandler(() => Ok(ValidBody));
        var sut = new GroqLeadTriage(new HttpClient(handler), Opts(), (_, _) => Task.CompletedTask);

        // Act
        var verdict = await sut.JudgeAsync(Item, Criteria, CancellationToken.None);

        // Assert
        verdict.Should().NotBeNull();
        verdict!.Decision.Should().Be(TriageDecision.Fit);
        verdict.Company.Should().Be("Acme BV");
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task JudgeAsync_NonSuccessStatus_ReturnsNull()
    {
        // Arrange
        var handler = new SequenceHandler(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = new GroqLeadTriage(new HttpClient(handler), Opts(), (_, _) => Task.CompletedTask);

        // Act
        var verdict = await sut.JudgeAsync(Item, Criteria, CancellationToken.None);

        // Assert
        verdict.Should().BeNull();
    }

    [Fact]
    public async Task JudgeAsync_SuccessWithUnparseableContent_ReturnsNull()
    {
        // Arrange — a success response whose content carries no recognisable VERDICT label
        var unparseableBody = """{"choices":[{"message":{"content":"I'm sorry, I can't help with that."}}]}""";
        var handler = new SequenceHandler(() => Ok(unparseableBody));
        var sut = new GroqLeadTriage(new HttpClient(handler), Opts(), (_, _) => Task.CompletedTask);

        // Act
        var verdict = await sut.JudgeAsync(Item, Criteria, CancellationToken.None);

        // Assert — unparseable is a triage FAILURE, never a synthetic rejection
        verdict.Should().BeNull();
    }

    [Fact]
    public async Task JudgeAsync_MissingApiKey_ReturnsNullWithoutAnyHttpCall()
    {
        // Arrange
        var handler = new SequenceHandler(() => Ok(ValidBody));
        var opts = Options.Create(new LlmOptions { ApiKey = "" });
        var sut = new GroqLeadTriage(new HttpClient(handler), opts, (_, _) => Task.CompletedTask);

        // Act
        var verdict = await sut.JudgeAsync(Item, Criteria, CancellationToken.None);

        // Assert
        verdict.Should().BeNull();
        handler.Calls.Should().Be(0);
    }
}

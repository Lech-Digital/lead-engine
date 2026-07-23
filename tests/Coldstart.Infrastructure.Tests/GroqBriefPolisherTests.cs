using System.Net;
using System.Text;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class GroqBriefPolisherTests
{
    private const string Ok = """{"choices":[{"message":{"content":"Acme helps dentists fill their calendars with AI-driven scheduling."}}]}""";

    private sealed class StubHandler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static IOptions<LlmOptions> Opts(string key = "test-key") => Options.Create(new LlmOptions { ApiKey = key });

    [Fact]
    public async Task PolishAsync_OnSuccess_ReturnsPolishedText()
    {
        // Arrange
        var sut = new GroqBriefPolisher(new HttpClient(new StubHandler(HttpStatusCode.OK, Ok)), Opts());

        // Act
        var result = await sut.PolishAsync("we do scheduling for dentists", CancellationToken.None);

        // Assert
        result.Should().Contain("Acme helps dentists");
    }

    [Fact]
    public async Task PolishAsync_WithNoApiKey_ReturnsNull()
    {
        // Arrange
        var sut = new GroqBriefPolisher(new HttpClient(new StubHandler(HttpStatusCode.OK, Ok)), Opts(key: ""));

        // Act
        var result = await sut.PolishAsync("anything", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task PolishAsync_OnHttpError_ReturnsNull()
    {
        // Arrange
        var sut = new GroqBriefPolisher(new HttpClient(new StubHandler(HttpStatusCode.InternalServerError, "")), Opts());

        // Act
        var result = await sut.PolishAsync("anything", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }
}

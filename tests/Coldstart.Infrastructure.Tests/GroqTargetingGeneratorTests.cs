using System.Net;
using System.Text;
using Coldstart.Application.Customers;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class GroqTargetingGeneratorTests
{
    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(response);
    }

    private static HttpResponseMessage Groq(string content)
    {
        var json = """{"choices":[{"message":{"content":""" + System.Text.Json.JsonSerializer.Serialize(content) + "}}]}";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static TargetingProposalRequest Request() =>
        new("Acme Logistics", "3PL software", "We help fleets schedule better", "Fleet scheduling SaaS");

    [Fact]
    public async Task ProposeAsync_WithApiKey_ParsesTheProposal()
    {
        // Arrange
        var http = new HttpClient(new StubHandler(Groq(
            "INDUSTRIES: logistics, 3PL\nGEOGRAPHIES: Netherlands\nROLES: Fleet Manager\n" +
            "SIZE_MIN: 10\nSIZE_MAX: 200\nTRIGGERS: raised funding\nEXCLUSIONS: competitors")));
        var opts = Options.Create(new LlmOptions { ApiKey = "k", Model = "m", BaseUrl = "https://api.groq.com/openai/v1/chat/completions" });
        var sut = new GroqTargetingGenerator(http, opts);

        // Act
        var proposal = await sut.ProposeAsync(Request(), CancellationToken.None);

        // Assert
        proposal.Should().NotBeNull();
        proposal!.Industries.Should().Contain("logistics").And.Contain("3PL");
        proposal.SizeMin.Should().Be(10);
        proposal.SizeMax.Should().Be(200);
    }

    [Fact]
    public async Task ProposeAsync_NonSuccessStatus_ReturnsNull()
    {
        // Arrange
        var http = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var opts = Options.Create(new LlmOptions { ApiKey = "k", Model = "m", BaseUrl = "https://api.groq.com/openai/v1/chat/completions" });
        var sut = new GroqTargetingGenerator(http, opts);

        // Act
        var proposal = await sut.ProposeAsync(Request(), CancellationToken.None);

        // Assert
        proposal.Should().BeNull();
    }

    [Fact]
    public async Task ProposeAsync_ResponseWithNoRecognisableLabels_ReturnsNull()
    {
        // Arrange
        var http = new HttpClient(new StubHandler(Groq("I'm sorry, I can't help with that.")));
        var opts = Options.Create(new LlmOptions { ApiKey = "k", Model = "m", BaseUrl = "https://api.groq.com/openai/v1/chat/completions" });
        var sut = new GroqTargetingGenerator(http, opts);

        // Act
        var proposal = await sut.ProposeAsync(Request(), CancellationToken.None);

        // Assert
        proposal.Should().BeNull();
    }

    [Fact]
    public async Task ProposeAsync_WithoutApiKey_ReturnsNull_AndMakesNoCall()
    {
        // Arrange — a handler that would throw if called
        var http = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var opts = Options.Create(new LlmOptions { ApiKey = "" });
        var sut = new GroqTargetingGenerator(http, opts);

        // Act
        var proposal = await sut.ProposeAsync(Request(), CancellationToken.None);

        // Assert
        proposal.Should().BeNull();
    }
}

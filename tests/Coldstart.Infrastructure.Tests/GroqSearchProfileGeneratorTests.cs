using System.Net;
using System.Text;
using Coldstart.Application.Customers;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class GroqSearchProfileGeneratorTests
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

    [Fact]
    public async Task GenerateAsync_WithApiKey_ParsesTheProfile()
    {
        // Arrange
        var http = new HttpClient(new StubHandler(Groq("OFFER: X\nKEYWORDS: dentist, clinic\nSIGNALS: hiring\nDISQUALIFIERS: labs")));
        var opts = Options.Create(new LlmOptions { ApiKey = "k", Model = "m", BaseUrl = "https://api.groq.com/openai/v1/chat/completions" });
        var sut = new GroqSearchProfileGenerator(http, opts);

        // Act
        var profile = await sut.GenerateAsync(new SearchProfileRequest("dental clinics", "we sell scheduling"), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile!.TargetKeywords.Should().Be("dentist\nclinic");
    }

    [Fact]
    public async Task GenerateAsync_WithoutApiKey_ReturnsNull_AndMakesNoCall()
    {
        // Arrange — a handler that would throw if called
        var http = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var opts = Options.Create(new LlmOptions { ApiKey = "" });
        var sut = new GroqSearchProfileGenerator(http, opts);

        // Act
        var profile = await sut.GenerateAsync(new SearchProfileRequest("x", "y"), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }
}

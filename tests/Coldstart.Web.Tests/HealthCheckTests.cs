using System.Net;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class HealthCheckTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Health_WithDatabaseUp_Returns200Healthy()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Health_IsAnonymous_NoLoginRedirect()
    {
        // Arrange — no auth cookie, no redirect following
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/health");

        // Assert — must answer directly (monitors don't follow login redirects)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

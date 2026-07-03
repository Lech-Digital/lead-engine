using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class PublicPagesTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task GetLanding_ReturnsOk_WithBrandAndCta()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        html.Should().Contain("Coldstart");
        html.Should().Contain("Send me an email");
    }

    [Fact]
    public async Task GetPrivacy_ReturnsOk_WithPolicyMarkers()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/privacy");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        html.Should().Contain("Privacy");
        html.Should().Contain("Schiedam");
    }
}

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
        var html = await client.GetStringAsync("/");

        // Assert
        html.Should().Contain("coldstart");                        // wordmark
        html.Should().Contain("Get 3 sample leads");               // primary CTA
        html.Should().Contain("Wake up to your next client");      // hero promise
    }

    [Fact]
    public async Task GetLanding_TellsTheMorningStory_AndPricing()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var html = await client.GetStringAsync("/");

        // Assert
        html.Should().Contain("While you sleep");                  // night act
        html.Should().Contain("06:58");                            // timestamped story
        html.Should().Contain("€750");                             // transparent pricing
        html.Should().Contain("€350");                             // setup fee
        html.Should().Contain("doing the part AI is actually good at");   // AI section (static markup is emitted verbatim — avoid asserting on apostrophes)
    }

    [Fact]
    public async Task GetLanding_HasHeroLoginLink_NotOnlyInStatusbar()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var html = await client.GetStringAsync("/");

        // Assert
        html.Should().Contain("hero-login");                     // the login button in the hero CTA row
        html.Should().Contain("/Identity/Account/Login");
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

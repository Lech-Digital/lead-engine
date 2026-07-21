using System.Net;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class BrandAssetsTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Theory]
    [InlineData("/fonts/CabinetGrotesk-Variable.woff2")]
    [InlineData("/fonts/Switzer-Variable.woff2")]
    public async Task GetFont_IsServedAsStaticAsset(string path)
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(path);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetLanding_ReferencesSelfHostedFonts_NotACdn()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var css = await client.GetStringAsync("/css/site.css");

        // Assert
        css.Should().Contain("/fonts/CabinetGrotesk-Variable.woff2");
        css.Should().Contain("/fonts/Switzer-Variable.woff2");
        css.Should().NotContain("fontshare.com");
        css.Should().NotContain("googleapis.com");
    }
}

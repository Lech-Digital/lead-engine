using System.Net;
using System.Xml.Linq;
using FluentAssertions;

namespace Coldstart.Web.Tests;

public sealed class SitemapTests
{
    [Fact]
    public void Xml_ContainsEveryPublicRoute_AsAbsoluteUrl()
    {
        // Arrange & Act
        var xml = Sitemap.Xml("https://getcoldstart.nl");

        // Assert — well-formed sitemap-protocol XML with all public pages
        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locs = doc.Root!.Elements(ns + "url").Select(u => u.Element(ns + "loc")!.Value).ToList();
        locs.Should().Contain("https://getcoldstart.nl/");
        foreach (var route in new[] { "about", "contact", "examples", "faq", "technology", "terms", "privacy", "verwerkersovereenkomst", "inquiry" })
            locs.Should().Contain($"https://getcoldstart.nl/{route}");
        locs.Should().OnlyContain(l => l.StartsWith("https://getcoldstart.nl/"));
    }

    [Fact]
    public void Xml_WithTrailingSlashOnBaseUrl_DoesNotDoubleTheSlash()
    {
        // Arrange & Act
        var xml = Sitemap.Xml("https://getcoldstart.nl/");

        // Assert
        xml.Should().Contain("https://getcoldstart.nl/about").And.NotContain("getcoldstart.nl//");
    }
}

public sealed class SeoEndpointsTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task GetSitemapXml_ShouldReturnXmlWithAbsoluteUrls()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/sitemap.xml");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");
        (await response.Content.ReadAsStringAsync()).Should().Contain("<loc>https://getcoldstart.nl/</loc>");
    }

    [Fact]
    public async Task GetRobotsTxt_ShouldDisallowPrivateAreas_AndPointAtTheSitemap()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/robots.txt");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Disallow: /app").And.Contain("Disallow: /admin")
            .And.Contain("Sitemap: https://getcoldstart.nl/sitemap.xml");
    }
}

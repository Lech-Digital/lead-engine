using Coldstart.Application.Leads;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class ContactLinkBuilderTests
{
    [Fact]
    public void Build_WithCompanyAndSource_ReturnsEncodedResearchLinks()
    {
        // Arrange / Act
        var links = ContactLinkBuilder.Build("Acme Co", "https://x/1");

        // Assert
        links.Should().HaveCount(3);
        links[0].Url.Should().StartWith("https://www.linkedin.com/search/results/people/?keywords=");
        links[0].Url.Should().Contain("Acme%20Co");
        links[1].Url.Should().StartWith("https://www.google.com/search?q=");
        links[2].Should().BeEquivalentTo(new ContactLink("Signal source", "https://x/1"));
    }

    [Fact]
    public void Build_WithNoSourceUrl_OmitsSignalSource()
    {
        // Arrange / Act
        var links = ContactLinkBuilder.Build("Acme Co", "");

        // Assert
        links.Should().HaveCount(2);
    }

    [Fact]
    public void Build_WithUnusableCompany_ReturnsEmpty()
    {
        // Arrange / Act
        var dash = ContactLinkBuilder.Build("-", "https://x/1");
        var blank = ContactLinkBuilder.Build("   ", "https://x/1");

        // Assert
        dash.Should().BeEmpty();
        blank.Should().BeEmpty();
    }
}

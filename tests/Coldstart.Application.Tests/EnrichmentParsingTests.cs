using Coldstart.Application.Harvest;
using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class EnrichmentParsingTests
{
    [Fact]
    public void Parse_WellFormed_ExtractsFiveFields()
    {
        // Arrange
        const string content = """
        FIT: YES
        SUMMARY: A funded startup scaling sales.
        SUBJECT: Congrats on the raise
        DRAFT: Saw your round. One question about pipeline?
        COMPANY: Acme
        """;

        // Act
        var r = EnrichmentParser.Parse(content);

        // Assert
        r.Should().NotBeNull();
        r!.Fit.Should().Be(Fit.Yes);
        r.Subject.Should().Be("Congrats on the raise");
        r.Company.Should().Be("Acme");
    }

    [Fact]
    public void Parse_FitNo_StillParsesAsNo()
    {
        // Arrange
        const string content = "FIT: NO\nSUMMARY: Job seeker.\nSUBJECT: -\nDRAFT: -\nCOMPANY: -";

        // Act
        var r = EnrichmentParser.Parse(content);

        // Assert
        r!.Fit.Should().Be(Fit.No);
    }

    [Fact]
    public void Parse_MissingFitLine_ReturnsNull()
    {
        // Act
        var r = EnrichmentParser.Parse("garbage with no labels");

        // Assert
        r.Should().BeNull();
    }
}

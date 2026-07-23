using Coldstart.Application.Customers;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class SearchProfileParserTests
{
    [Fact]
    public void Parse_ExtractsAllFour_AndJoinsListsWithNewlines()
    {
        // Arrange
        var content = """
            OFFER: scheduling software for dental clinics
            KEYWORDS: dentist, dental clinic, orthodontist
            SIGNALS: hiring, opening a new location
            DISQUALIFIERS: hospital groups; dental labs
            """;

        // Act
        var profile = SearchProfileParser.Parse(content);

        // Assert
        profile.Should().NotBeNull();
        profile!.Offer.Should().Be("scheduling software for dental clinics");
        profile.TargetKeywords.Should().Be("dentist\ndental clinic\northodontist");
        profile.BuyingSignals.Should().Be("hiring\nopening a new location");
        profile.Disqualifiers.Should().Be("hospital groups\ndental labs");
    }

    [Fact]
    public void Parse_WhenOfferOrKeywordsMissing_ReturnsNull()
    {
        // Arrange — no OFFER/KEYWORDS labels present
        var content = "SIGNALS: hiring\nDISQUALIFIERS: none";

        // Act
        var profile = SearchProfileParser.Parse(content);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public void Parse_ToleratesMissingOptionalSections()
    {
        // Arrange — only the required OFFER + KEYWORDS
        var content = "OFFER: X\nKEYWORDS: a, b";

        // Act
        var profile = SearchProfileParser.Parse(content);

        // Assert
        profile.Should().NotBeNull();
        profile!.BuyingSignals.Should().BeEmpty();
        profile.Disqualifiers.Should().BeEmpty();
    }
}

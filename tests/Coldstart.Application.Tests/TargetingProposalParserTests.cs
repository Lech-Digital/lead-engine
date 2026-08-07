using Coldstart.Application.Customers;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class TargetingProposalParserTests
{
    [Fact]
    public void Parse_LabelledOutput_FillsEveryField()
    {
        // Arrange
        var content = """
            INDUSTRIES: logistics, 3PL
            GEOGRAPHIES: Netherlands, Belgium
            ROLES: Fleet Manager, Head of Operations
            SIZE_MIN: 10
            SIZE_MAX: 200
            TRIGGERS: raised funding, hiring fleet staff
            EXCLUSIONS: competitors, consultancies
            """;

        // Act
        var input = TargetingProposalParser.Parse(content);

        // Assert
        input.Should().NotBeNull();
        input!.Industries.Should().Contain("logistics").And.Contain("3PL");
        input.SizeMin.Should().Be(10);
        input.SizeMax.Should().Be(200);
        input.TriggerEvents.Should().Contain("raised funding");
        input.Exclusions.Should().Contain("competitors");
    }

    [Fact]
    public void Parse_MissingOptionalSections_StillReturnsWhatWasGiven()
    {
        // Arrange
        var content = "INDUSTRIES: logistics\nTRIGGERS: raised funding";

        // Act
        var input = TargetingProposalParser.Parse(content);

        // Assert
        input.Should().NotBeNull();
        input!.Industries.Should().Contain("logistics");
        input.Geographies.Should().BeNullOrEmpty();
        input.SizeMin.Should().BeNull();
    }

    [Fact]
    public void Parse_NoRecognisableLabels_ReturnsNullRatherThanAnEmptyProfile()
    {
        // Act
        var input = TargetingProposalParser.Parse("I'm sorry, I can't help with that.");

        // Assert — an unparseable response is a failure, never a silently empty profile
        input.Should().BeNull();
    }

    [Fact]
    public void Parse_NonNumericSize_IgnoresTheSizeRatherThanFailingTheWholeProposal()
    {
        // Arrange
        var content = "INDUSTRIES: logistics\nSIZE_MIN: lots";

        // Act
        var input = TargetingProposalParser.Parse(content);

        // Assert
        input.Should().NotBeNull();
        input!.SizeMin.Should().BeNull();
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsNull()
    {
        // Act / Assert
        TargetingProposalParser.Parse("").Should().BeNull();
    }
}

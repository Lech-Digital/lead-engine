using Coldstart.Application.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class TriageVerdictParserTests
{
    [Fact]
    public void Parse_FitVerdict_ReturnsCompanyAndCriteria()
    {
        // Arrange
        var content = """
            VERDICT: FIT
            COMPANY: Acme BV
            MET: early stage, Netherlands
            FAILED: none
            """;

        // Act
        var verdict = TriageVerdictParser.Parse(content);

        // Assert
        verdict.Should().NotBeNull();
        verdict!.Decision.Should().Be(TriageDecision.Fit);
        verdict.Company.Should().Be("Acme BV");
        verdict.Met.Should().Contain("early stage");
    }

    [Fact]
    public void Parse_NotFitVerdict_CarriesTheFailedCriteria()
    {
        // Arrange
        var content = "VERDICT: NOT FIT\nCOMPANY: Menlo Ventures\nMET: none\nFAILED: is an investor, not a prospect";

        // Act
        var verdict = TriageVerdictParser.Parse(content);

        // Assert
        verdict.Should().NotBeNull();
        verdict!.Decision.Should().Be(TriageDecision.NotFit);
        verdict.Failed.Should().Contain(f => f.Contains("investor"));
    }

    [Fact]
    public void Parse_UnsureVerdict_IsDistinctFromNotFit()
    {
        // Act
        var verdict = TriageVerdictParser.Parse("VERDICT: UNSURE\nCOMPANY: Acme");

        // Assert — unsure must not be silently treated as a rejection
        verdict!.Decision.Should().Be(TriageDecision.Unsure);
    }

    [Fact]
    public void Parse_NoRecognisableVerdict_ReturnsNull()
    {
        // Act / Assert — an unparseable answer is a failure, never a silent rejection
        TriageVerdictParser.Parse("I'm sorry, I can't help with that.").Should().BeNull();
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsNull()
    {
        // Act / Assert
        TriageVerdictParser.Parse("").Should().BeNull();
    }
}

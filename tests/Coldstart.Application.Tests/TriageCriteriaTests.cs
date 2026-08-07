using Coldstart.Application.Harvest;
using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class TriageCriteriaTests
{
    [Fact]
    public void Describe_FullyPopulatedTargeting_EmitsOneLinePerDimension()
    {
        // Arrange
        var targeting = Targeting.Create(
            industries: ["SaaS", "Fintech"],
            geographies: ["Netherlands"],
            buyerRoles: ["VP Sales"],
            sizeMin: 50,
            sizeMax: 200,
            triggerEvents: ["raised Series A"],
            exclusions: ["competitors"]);

        // Act
        var criteria = TriageCriteria.Describe(targeting, "outbound lead gen");

        // Assert
        criteria.Should().Be(
            "Offer: outbound lead gen\n" +
            "Industries: SaaS, Fintech\n" +
            "Geography: Netherlands\n" +
            "Buyer roles: VP Sales\n" +
            "Company size: 50-200 employees\n" +
            "Trigger events: raised Series A\n" +
            "Exclusions: competitors");
    }

    [Fact]
    public void Describe_EmptyTargeting_OmitsEveryDimensionButTheOffer()
    {
        // Act
        var criteria = TriageCriteria.Describe(Targeting.Empty, "outbound lead gen");

        // Assert — no dimension is fabricated just to fill a line
        criteria.Should().Be("Offer: outbound lead gen");
    }

    [Fact]
    public void Describe_OnlySizeMinSet_StatesSizeAsAnOpenEndedRange()
    {
        // Arrange
        var targeting = Targeting.Create([], [], [], sizeMin: 50, sizeMax: null, [], []);

        // Act
        var criteria = TriageCriteria.Describe(targeting, "");

        // Assert
        criteria.Should().Be("Company size: 50+ employees");
    }

    [Fact]
    public void Describe_OnlySizeMaxSet_StatesSizeAsAnUpperBound()
    {
        // Arrange
        var targeting = Targeting.Create([], [], [], sizeMin: null, sizeMax: 200, [], []);

        // Act
        var criteria = TriageCriteria.Describe(targeting, "");

        // Assert
        criteria.Should().Be("Company size: up to 200 employees");
    }

    [Fact]
    public void Describe_NeitherSizeBoundSet_OmitsTheCompanySizeLineEntirely()
    {
        // Act
        var criteria = TriageCriteria.Describe(Targeting.Empty, "");

        // Assert
        criteria.Should().NotContain("Company size");
    }

    [Fact]
    public void Describe_BlankOffer_OmitsTheOfferLine()
    {
        // Arrange
        var targeting = Targeting.Create(["SaaS"], [], [], null, null, [], []);

        // Act
        var criteria = TriageCriteria.Describe(targeting, "   ");

        // Assert
        criteria.Should().Be("Industries: SaaS");
    }

    [Fact]
    public void Describe_CalledTwiceWithTheSameInputs_ProducesTheSameOutput()
    {
        // Arrange — pure and deterministic: no clock, no randomness, no hidden state
        var targeting = Targeting.Create(["SaaS"], ["Netherlands"], [], 10, 20, [], []);

        // Act
        var first = TriageCriteria.Describe(targeting, "offer");
        var second = TriageCriteria.Describe(targeting, "offer");

        // Assert
        first.Should().Be(second);
    }
}

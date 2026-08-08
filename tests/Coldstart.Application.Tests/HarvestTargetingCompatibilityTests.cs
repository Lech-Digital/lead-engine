using Coldstart.Application.Harvest;
using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class HarvestTargetingCompatibilityTests
{
    private static CompanyProfile ProfileWith(string keywords, string signals, string disqualifiers) =>
        CompanyProfile.Empty with
        {
            CompanyName = "Acme",
            Offer = "route planning software",
            TargetKeywords = keywords,
            BuyingSignals = signals,
            Disqualifiers = disqualifiers,
        };

    [Fact]
    public void FromCustomer_NoStructuredTargeting_MatchesTheLegacyPathExactly()
    {
        // Arrange
        var profile = ProfileWith("logistics\n3PL", "raised funding", "competitors");

        // Act
        var legacy = HarvestTargeting.FromProfile(profile, "Acme");
        var actual = HarvestTargeting.FromCustomer(profile, Targeting.Empty, "Acme");

        // Assert — byte-for-byte the same behaviour for every existing customer
        actual.Should().BeEquivalentTo(legacy);
    }

    [Fact]
    public void FromCustomer_StructuredTargetingWithSameData_AgreesWithTheLegacyPath()
    {
        // Arrange — the same intent expressed both ways
        var profile = ProfileWith("logistics\n3PL", "raised funding", "competitors");
        var targeting = Targeting.Create(["logistics", "3PL"], [], [], null, null, ["raised funding"], ["competitors"]);

        // Act
        var legacy = HarvestTargeting.FromProfile(profile, "Acme");
        var structured = HarvestTargeting.FromCustomer(profile, targeting, "Acme");

        // Assert — sub-project A must not move lead flow in either direction; whole-record
        // equivalence so a future edit to SenderName/SenderTitle/MinScore/MaxEnrichPerRun
        // can't silently diverge behind a green suite.
        structured.Should().BeEquivalentTo(legacy);
    }

    [Fact]
    public void FromCustomer_StructuredTargetingPresent_KeywordsComeFromLegacyWhileSignalsAndExclusionsComeFromStructure()
    {
        // Arrange — industries/geographies are category nouns, not search-intent phrases;
        // only the legacy curated keywords should feed the rotation.
        var profile = ProfileWith("struggling with outbound", "", "");
        var targeting = Targeting.Create(["logistics"], ["NL"], ["Fleet Manager"], 10, 200,
            ["raised funding"], ["competitors"]);

        // Act
        var actual = HarvestTargeting.FromCustomer(profile, targeting, "Acme");

        // Assert
        actual.TargetKeywords.Should().BeEquivalentTo(["struggling with outbound"]);
        actual.BuyingSignals.Should().Contain("raised funding");
        actual.Disqualifiers.Should().Contain("competitors");
        actual.HasKeywords.Should().BeTrue();
    }

    [Fact]
    public void FromCustomer_StructuredTargetingWithIndustriesAndGeographies_TargetKeywordsExcludeCategoryNouns()
    {
        // Arrange — regression guard: sending "Netherlands"/"B2B SaaS" as literal search
        // queries returns noise, while "struggling with outbound" finds a founder with a
        // problem. This must fail if IndustryList/GeographyList are ever concatenated back
        // into TargetKeywords.
        var profile = ProfileWith("struggling with outbound", "", "");
        var targeting = Targeting.Create(["B2B SaaS"], ["Netherlands"], [], null, null, [], []);

        // Act
        var actual = HarvestTargeting.FromCustomer(profile, targeting, "Acme");

        // Assert
        actual.TargetKeywords.Should().Contain("struggling with outbound");
        actual.TargetKeywords.Should().NotContain("B2B SaaS");
        actual.TargetKeywords.Should().NotContain("Netherlands");
    }

    [Fact]
    public void FromCustomer_EmptyProfileAndEmptyTargeting_ProducesNoKeywords()
    {
        // Arrange
        var profile = ProfileWith("", "", "");

        // Act
        var actual = HarvestTargeting.FromCustomer(profile, Targeting.Empty, "Acme");

        // Assert — the harvest's own "no keywords" guard still applies downstream
        actual.HasKeywords.Should().BeFalse();
    }
}

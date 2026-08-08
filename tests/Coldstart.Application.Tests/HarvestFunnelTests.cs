using Coldstart.Application.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class HarvestFunnelTests
{
    [Fact]
    public void CountDropped_SameReasonTwice_AggregatesUnderThatReason()
    {
        // Arrange
        var funnel = new HarvestFunnel();

        // Act
        funnel.CountDropped("excluded");
        funnel.CountDropped("excluded");
        funnel.CountDropped("duplicate-company");

        // Assert
        funnel.DropReasons["excluded"].Should().Be(2);
        funnel.DropReasons["duplicate-company"].Should().Be(1);
    }

    [Fact]
    public void CountSavedFrom_MultipleSources_TracksEachSeparately()
    {
        // Arrange
        var funnel = new HarvestFunnel();

        // Act
        funnel.CountSavedFrom("funding");
        funnel.CountSavedFrom("hackernews");
        funnel.CountSavedFrom("funding");

        // Assert — the headline measure: leads must come from more than one source
        funnel.PerSource["funding"].Should().Be(2);
        funnel.PerSource["hackernews"].Should().Be(1);
    }

    [Fact]
    public void NewFunnel_BeforeAnyCounting_IsAllZeroAndEmpty()
    {
        // Act
        var funnel = new HarvestFunnel();

        // Assert
        funnel.Harvested.Should().Be(0);
        funnel.DropReasons.Should().BeEmpty();
        funnel.PerSource.Should().BeEmpty();
    }

    [Fact]
    public void ToReport_AfterCounting_CarriesCountsAndReasonsThrough()
    {
        // Arrange
        var funnel = new HarvestFunnel();
        funnel.CountHarvested();
        funnel.CountDropped("stale");
        funnel.CountSavedFrom("adzuna");

        // Act
        var report = funnel.ToReport(savedLeads: []);

        // Assert
        report.Harvested.Should().Be(1);
        report.DropReasons["stale"].Should().Be(1);
        report.PerSource["adzuna"].Should().Be(1);
    }

    [Fact]
    public void CountDropped_PreScoreAndPostEnrichmentSuppression_TrackSeparately()
    {
        // Arrange — the two suppression drops mean genuinely different things operationally:
        // one costs nothing, the other means an LLM call was already paid for. They must never
        // collapse into a single "suppressed" bucket.
        var funnel = new HarvestFunnel();

        // Act
        funnel.CountDropped("suppressed");
        funnel.CountDropped("suppressed-after-enrichment");
        funnel.CountDropped("suppressed-after-enrichment");

        // Assert
        funnel.DropReasons["suppressed"].Should().Be(1);
        funnel.DropReasons["suppressed-after-enrichment"].Should().Be(2);
    }
}

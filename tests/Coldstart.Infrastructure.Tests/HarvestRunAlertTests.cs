using Coldstart.Application.Harvest;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class HarvestRunAlertTests
{
    private static readonly Guid CustomerId = Guid.NewGuid();

    [Fact]
    public void For_WhenCandidatesExistedButNothingEnriched_BlamesTheLlm()
    {
        // Arrange — 10 scored in, 2 deduped → 8 real candidates reached the enricher and all failed
        var report = new HarvestReport(Harvested: 20, ScoredIn: 10, Deduped: 2, Enriched: 0, Saved: 0, Rejected: 0);

        // Act
        var alert = HarvestRunAlert.For(report, "Acme", CustomerId);

        // Assert
        alert.Should().NotBeNull();
        alert!.Value.Subject.Should().Contain("LLM");
        alert.Value.Body.Should().Contain("8 candidate");
    }

    [Fact]
    public void For_WhenEverythingDeduped_ReportsStaleSourcesInsteadOfBlamingTheLlm()
    {
        // Arrange — the 2026-08-02/03 incident: all scored-in items were already-known fingerprints
        var report = new HarvestReport(Harvested: 95, ScoredIn: 48, Deduped: 48, Enriched: 0, Saved: 0, Rejected: 0);

        // Act
        var alert = HarvestRunAlert.For(report, "Acme", CustomerId);

        // Assert — the honest diagnosis: content starvation, not LLM quota
        alert.Should().NotBeNull();
        alert!.Value.Subject.Should().Contain("nothing new").And.NotContain("LLM");
        alert.Value.Body.Should().Contain("dedup");
    }

    [Fact]
    public void For_WhenEnrichmentSucceeded_ReturnsNoAlert()
    {
        // Arrange
        var report = new HarvestReport(Harvested: 20, ScoredIn: 10, Deduped: 2, Enriched: 5, Saved: 3, Rejected: 2);

        // Act & Assert
        HarvestRunAlert.For(report, "Acme", CustomerId).Should().BeNull();
    }

    [Fact]
    public void For_WhenNothingScoredIn_ReturnsNoAlert()
    {
        // Arrange — a quiet run with no candidates at all is not an incident
        var report = new HarvestReport(Harvested: 5, ScoredIn: 0, Deduped: 0, Enriched: 0, Saved: 0, Rejected: 0);

        // Act & Assert
        HarvestRunAlert.For(report, "Acme", CustomerId).Should().BeNull();
    }
}

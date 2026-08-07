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
        // Arrange — 10 scored in, 2 deduped → 8 real candidates, all 8 sent to the enricher and all failed
        var report = new HarvestReport(Harvested: 20, ScoredIn: 10, Deduped: 2, Enriched: 0, Saved: 0, Rejected: 0, Attempted: 8);

        // Act
        var alert = HarvestRunAlert.For(report, "Acme", CustomerId);

        // Assert
        alert.Should().NotBeNull();
        alert!.Value.Subject.Should().Contain("LLM");
        alert.Value.Body.Should().Contain("8 candidate");
        alert.Value.Body.Should().Contain("8 of them were sent to the enricher");
    }

    [Fact]
    public void For_WhenCandidatePoolExceedsTheEnrichCap_ReportsAttemptedCountNotTheWholePool()
    {
        // Arrange — Task 3 revived four sources: 180 items now survive veto+dedup in one run, but
        // MaxEnrichPerRun only ever sends 30 to the enricher. The message must not claim all 180
        // were tried and failed — that is the exact misdiagnosis the alert exists to prevent.
        var report = new HarvestReport(Harvested: 400, ScoredIn: 180, Deduped: 0, Enriched: 0, Saved: 0, Rejected: 0, Attempted: 30);

        // Act
        var alert = HarvestRunAlert.For(report, "Acme", CustomerId);

        // Assert — subject and body report the attempted count, and the body still names the
        // full pool separately rather than conflating the two.
        alert.Should().NotBeNull();
        alert!.Value.Subject.Should().Contain("0 of 30 attempted").And.NotContain("180");
        alert.Value.Body.Should().Contain("180 candidate").And.Contain("30 of them were sent to the enricher");
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

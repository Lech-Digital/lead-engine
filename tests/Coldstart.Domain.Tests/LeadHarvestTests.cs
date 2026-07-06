using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class LeadHarvestTests
{
    [Fact]
    public void CreateHarvested_SetsProvenanceAndPendingReview()
    {
        // Act
        var lead = Lead.CreateHarvested(CustomerId.New(), "Patronus AI", Fit.Yes, "Subj", "Body",
            "hackernews", "https://news.ycombinator.com/item?id=1", "abc123", 0.72, "hiring,outbound",
            DateTimeOffset.UtcNow);

        // Assert
        lead.Source.Should().Be("hackernews");
        lead.Fingerprint.Should().Be("abc123");
        lead.Score.Should().Be(0.72);
        lead.MatchedSignals.Should().Be("hiring,outbound");
        lead.Status.Should().Be(LeadStatus.PendingReview);
    }
}

using Coldstart.Application.Leads;
using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class LeadLockingTests
{
    private static Lead Harvested(Fit fit, double score, DateTimeOffset created) =>
        Lead.CreateHarvested(new CustomerId(Guid.NewGuid()), "Co", fit, "s", "b",
            "hn", "https://x/1", Guid.NewGuid().ToString("N"), score, "sig", created);

    [Fact]
    public void LockedLeadIds_FreePlan_LocksEverythingBeyondTheFreeCount()
    {
        // Arrange — 5 leads, quota 3
        var now = DateTimeOffset.UtcNow;
        var leads = new[]
        {
            Harvested(Fit.Yes, 0.9, now), Harvested(Fit.Yes, 0.8, now),
            Harvested(Fit.Maybe, 0.7, now), Harvested(Fit.Maybe, 0.6, now),
            Harvested(Fit.No, 0.5, now),
        };

        // Act
        var locked = LeadLocking.LockedLeadIds(leads, CustomerPlan.Free, 3);

        // Assert — top 3 unlocked, last 2 locked
        locked.Should().HaveCount(2);
        locked.Should().Contain(leads[3].Id.Value).And.Contain(leads[4].Id.Value);
        locked.Should().NotContain(leads[0].Id.Value);
    }

    [Fact]
    public void LockedLeadIds_RanksByFitThenScore_NotInsertionOrder()
    {
        // Arrange — a low-fit high-score lead must NOT outrank a high-fit lead
        var now = DateTimeOffset.UtcNow;
        var no = Harvested(Fit.No, 0.99, now);
        var yes = Harvested(Fit.Yes, 0.10, now);

        // Act — quota 1
        var locked = LeadLocking.LockedLeadIds(new[] { no, yes }, CustomerPlan.Free, 1);

        // Assert — the Yes is unlocked, the No is locked
        locked.Should().Contain(no.Id.Value);
        locked.Should().NotContain(yes.Id.Value);
    }

    [Fact]
    public void LockedLeadIds_FullPlan_LocksNothing()
    {
        // Arrange
        var leads = new[] { Harvested(Fit.Yes, 0.9, DateTimeOffset.UtcNow), Harvested(Fit.No, 0.1, DateTimeOffset.UtcNow) };

        // Act
        var locked = LeadLocking.LockedLeadIds(leads, CustomerPlan.Full, 1);

        // Assert
        locked.Should().BeEmpty();
    }

    [Fact]
    public void LockedLeadIds_FewerLeadsThanQuota_LocksNothing()
    {
        // Arrange
        var leads = new[] { Harvested(Fit.Yes, 0.9, DateTimeOffset.UtcNow) };

        // Act
        var locked = LeadLocking.LockedLeadIds(leads, CustomerPlan.Free, 3);

        // Assert
        locked.Should().BeEmpty();
    }
}

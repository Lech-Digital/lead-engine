using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class LeadOutcomeTests
{
    private static Lead SentLead()
    {
        var lead = Lead.CreateHarvested(CustomerId.New(), "Acme", Fit.Yes, "s", "d",
            "hackernews", "https://x", "fp", 0.8, "outbound", DateTimeOffset.UtcNow);
        lead.Approve(new EmailAddress("ceo@x.com"));
        lead.MarkSent(DateTimeOffset.UtcNow.AddDays(-5));
        return lead;
    }

    [Fact]
    public void RecordOutcome_OnSentLead_StoresOutcomeAndTimestamp()
    {
        // Arrange
        var lead = SentLead();
        var at = DateTimeOffset.UtcNow;

        // Act
        lead.RecordOutcome(LeadOutcome.Replied, at);

        // Assert
        lead.Outcome.Should().Be(LeadOutcome.Replied);
        lead.OutcomeAt.Should().Be(at);
    }

    [Fact]
    public void RecordOutcome_None_ClearsOutcomeAndTimestamp()
    {
        // Arrange
        var lead = SentLead();
        lead.RecordOutcome(LeadOutcome.Won, DateTimeOffset.UtcNow);

        // Act
        lead.RecordOutcome(LeadOutcome.None, DateTimeOffset.UtcNow);

        // Assert
        lead.Outcome.Should().Be(LeadOutcome.None);
        lead.OutcomeAt.Should().BeNull();
    }

    [Fact]
    public void RecordOutcome_OnUnsentLead_Throws()
    {
        // Arrange
        var lead = Lead.CreateHarvested(CustomerId.New(), "Acme", Fit.Yes, "s", "d",
            "hackernews", "https://x", "fp", 0.8, "outbound", DateTimeOffset.UtcNow);

        // Act
        Action act = () => lead.RecordOutcome(LeadOutcome.Replied, DateTimeOffset.UtcNow);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Discard_WithReason_StoresIt_AndReopenClearsIt()
    {
        // Arrange
        var lead = Lead.CreateHarvested(CustomerId.New(), "Acme", Fit.Yes, "s", "d",
            "hackernews", "https://x", "fp", 0.8, "outbound", DateTimeOffset.UtcNow);

        // Act & Assert
        lead.Discard("Too big");
        lead.SkipReason.Should().Be("Too big");
        lead.Reopen();
        lead.SkipReason.Should().BeNull();
    }
}

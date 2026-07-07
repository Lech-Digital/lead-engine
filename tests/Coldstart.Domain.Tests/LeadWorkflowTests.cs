using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class LeadWorkflowTests
{
    private static Lead Harvested() => Lead.CreateHarvested(
        CustomerId.New(), "Acme", Fit.Yes, "subj", "body",
        "hackernews", "https://x/1", "fp-1", 0.7, "outbound", DateTimeOffset.UtcNow);

    [Fact]
    public void Approve_WithoutRecipient_ApprovesAndLeavesRecipientNull()
    {
        // Arrange
        var lead = Harvested();

        // Act
        lead.Approve();

        // Assert
        lead.Status.Should().Be(LeadStatus.Approved);
        lead.RecipientEmail.Should().BeNull();
    }

    [Fact]
    public void Approve_FromShelved_RescuesToApproved()
    {
        // Arrange
        var lead = Harvested();
        lead.Discard();

        // Act
        lead.Approve(new EmailAddress("ceo@acme.com"));

        // Assert
        lead.Status.Should().Be(LeadStatus.Approved);
        lead.RecipientEmail!.Value.Should().Be("ceo@acme.com");
    }

    [Fact]
    public void Discard_SetsShelved_NotRejected()
    {
        // Arrange
        var lead = Harvested();

        // Act
        lead.Discard();

        // Assert
        lead.Status.Should().Be(LeadStatus.Shelved);
    }

    [Fact]
    public void Reopen_FromRejected_ReturnsToPendingReview()
    {
        // Arrange
        var lead = Harvested();
        lead.Reject();

        // Act
        lead.Reopen();

        // Assert
        lead.Status.Should().Be(LeadStatus.PendingReview);
    }

    [Fact]
    public void MarkSent_WithoutRecipient_Throws()
    {
        // Arrange
        var lead = Harvested();
        lead.Approve();

        // Act
        var act = () => lead.MarkSent(DateTimeOffset.UtcNow);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ReviseDraft_AfterSent_Throws()
    {
        // Arrange
        var lead = Harvested();
        lead.Approve(new EmailAddress("ceo@acme.com"));
        lead.MarkSent(DateTimeOffset.UtcNow);

        // Act
        var act = () => lead.ReviseDraft("new", "new body");

        // Assert
        act.Should().Throw<DomainException>();
    }
}

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
    public void Reopen_FromShelved_ReturnsToPendingReview()
    {
        // Arrange — the customer skipped it themselves; the draft is intact
        var lead = Harvested();
        lead.Discard("Bad timing");

        // Act
        lead.Reopen();

        // Assert
        lead.Status.Should().Be(LeadStatus.PendingReview);
        lead.SkipReason.Should().BeNull();
    }

    [Fact]
    public void Reopen_FromRejected_Throws()
    {
        // Arrange — the engine judged it a non-fit, so it was saved with "-" subject and draft
        var lead = Lead.CreateHarvested(CustomerId.New(), "Acme", Fit.No, "-", "-",
            "hackernews", "https://x/1", "fp-1", 0.7, "outbound", DateTimeOffset.UtcNow);
        lead.Reject();

        // Act
        var act = () => lead.Reopen();

        // Assert — reopening would put a blank lead into the customer's briefing
        act.Should().Throw<DomainException>().WithMessage("*no draft to review*");
        lead.Status.Should().Be(LeadStatus.Rejected);
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

    [Fact]
    public void SetRecipient_WhenPendingReview_UpdatesRecipient()
    {
        // Arrange
        var lead = Harvested();

        // Act
        lead.SetRecipient(new EmailAddress("ceo@acme.com"));

        // Assert
        lead.RecipientEmail!.Value.Should().Be("ceo@acme.com");
    }

    [Fact]
    public void SetRecipient_WhenSent_Throws()
    {
        // Arrange
        var lead = Harvested();
        lead.Approve(new EmailAddress("ceo@acme.com"));
        lead.MarkSent(DateTimeOffset.UtcNow);

        // Act
        var act = () => lead.SetRecipient(new EmailAddress("new@acme.com"));

        // Assert
        act.Should().Throw<DomainException>();
    }
}

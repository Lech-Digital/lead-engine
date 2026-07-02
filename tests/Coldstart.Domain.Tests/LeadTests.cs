using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class LeadTests
{
    private static Lead NewLead() =>
        Lead.Create(CustomerId.New(), "Patronus AI", Fit.Yes, "Subject", "Body",
            new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Create_SetsPendingReview_AndNoEmailConfidence()
    {
        // Act
        var lead = NewLead();

        // Assert
        lead.Status.Should().Be(LeadStatus.PendingReview);
        lead.EmailConfidence.Should().Be(EmailConfidence.None);
        lead.RecipientEmail.Should().BeNull();
    }

    [Fact]
    public void Approve_WithRecipient_SetsApprovedAndRecipient()
    {
        // Arrange
        var lead = NewLead();

        // Act
        lead.Approve(new EmailAddress("anand@patronus.ai"));

        // Assert
        lead.Status.Should().Be(LeadStatus.Approved);
        lead.RecipientEmail!.Value.Should().Be("anand@patronus.ai");
    }

    [Fact]
    public void MarkSent_WhenNotApproved_Throws()
    {
        // Arrange
        var lead = NewLead();

        // Act
        var act = () => lead.MarkSent(DateTimeOffset.UtcNow);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Approve_WhenAlreadySent_Throws()
    {
        // Arrange
        var lead = NewLead();
        lead.Approve(new EmailAddress("anand@patronus.ai"));
        lead.MarkSent(DateTimeOffset.UtcNow);

        // Act
        var act = () => lead.Approve(new EmailAddress("other@patronus.ai"));

        // Assert
        act.Should().Throw<DomainException>();
    }
}

using Coldstart.Application.Leads;
using Coldstart.Application.Outreach;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class LeadWorkflowSendTests
{
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static Lead ApprovedLead(string recipient = "ceo@acme.com")
    {
        var lead = Lead.CreateHarvested(new CustomerId(CustomerId), "Acme", Fit.Yes,
            "About your raise", "Body copy.", "hn", "https://x/1", "fp1", 0.7, "outbound", DateTimeOffset.UtcNow);
        lead.Approve(new EmailAddress(recipient));
        return lead;
    }

    private static (LeadWorkflow sut, ILeadRepository repo, IOutreachMailer mailer) Sut(Lead? lead)
    {
        var repo = Substitute.For<ILeadRepository>();
        if (lead is not null) repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var mailer = Substitute.For<IOutreachMailer>();
        return (new LeadWorkflow(repo, mailer), repo, mailer);
    }

    [Fact]
    public async Task SendFromColdstartAsync_WhenApprovedWithRecipient_SendsStoredContentAndMarksSent()
    {
        // Arrange
        var lead = ApprovedLead();
        var (sut, repo, mailer) = Sut(lead);

        // Act
        var result = await sut.SendFromColdstartAsync(CustomerId, lead.Id.Value, CancellationToken.None);

        // Assert
        result.IsOk.Should().BeTrue();
        await mailer.Received(1).SendAsync(
            Arg.Is<OutreachEmail>(m => m.To == "ceo@acme.com" && m.Subject == "About your raise" && m.Body == "Body copy."),
            Arg.Any<CancellationToken>());
        lead.Status.Should().Be(LeadStatus.Sent);
        lead.SentAt.Should().NotBeNull();
        await repo.Received().UpdateAsync(lead, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendFromColdstartAsync_WhenNotApproved_ReturnsInvalid_AndDoesNotSend()
    {
        // Arrange — a pending lead
        var lead = Lead.CreateHarvested(new CustomerId(CustomerId), "Acme", Fit.Yes, "s", "b",
            "hn", "https://x/1", "fp2", 0.7, "outbound", DateTimeOffset.UtcNow);
        var (sut, _, mailer) = Sut(lead);

        // Act
        var result = await sut.SendFromColdstartAsync(CustomerId, lead.Id.Value, CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.Invalid);
        lead.Status.Should().Be(LeadStatus.PendingReview);
        await mailer.DidNotReceive().SendAsync(Arg.Any<OutreachEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendFromColdstartAsync_WhenSendThrows_ReturnsInvalid_AndLeavesLeadApproved()
    {
        // Arrange
        var lead = ApprovedLead();
        var (sut, repo, mailer) = Sut(lead);
        mailer.SendAsync(Arg.Any<OutreachEmail>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("smtp down"));

        // Act
        var result = await sut.SendFromColdstartAsync(CustomerId, lead.Id.Value, CancellationToken.None);

        // Assert — failed send never marks Sent
        result.Status.Should().Be(LeadActionStatus.Invalid);
        lead.Status.Should().Be(LeadStatus.Approved);
        lead.SentAt.Should().BeNull();
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Lead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendFromColdstartAsync_WhenApprovedWithNoRecipient_ReturnsInvalid_AndDoesNotSend()
    {
        // Arrange — approved without ever setting a recipient
        var lead = Lead.CreateHarvested(new CustomerId(CustomerId), "Acme", Fit.Yes,
            "About your raise", "Body copy.", "hn", "https://x/1", "fp3", 0.7, "outbound", DateTimeOffset.UtcNow);
        lead.Approve();
        var (sut, _, mailer) = Sut(lead);

        // Act
        var result = await sut.SendFromColdstartAsync(CustomerId, lead.Id.Value, CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.Invalid);
        await mailer.DidNotReceive().SendAsync(Arg.Any<OutreachEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendFromColdstartAsync_WhenLeadNotOwned_ReturnsNotFound()
    {
        // Arrange — repo returns null (not found / not this customer)
        var (sut, _, mailer) = Sut(lead: null);

        // Act
        var result = await sut.SendFromColdstartAsync(CustomerId, Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.NotFound);
        await mailer.DidNotReceive().SendAsync(Arg.Any<OutreachEmail>(), Arg.Any<CancellationToken>());
    }
}

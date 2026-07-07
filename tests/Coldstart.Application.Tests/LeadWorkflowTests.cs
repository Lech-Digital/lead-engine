using Coldstart.Application.Leads;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class LeadWorkflowTests
{
    private static Lead LeadFor(CustomerId customerId) => Lead.CreateHarvested(
        customerId, "Acme", Fit.Yes, "subj", "body",
        "hackernews", "https://x/1", "fp-1", 0.7, "outbound", DateTimeOffset.UtcNow);

    [Fact]
    public async Task AcceptAsync_WithoutRecipient_ApprovesAndPersists()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var sut = new LeadWorkflow(repo);

        // Act
        var result = await sut.AcceptAsync(customerId, lead.Id.Value, "s2", "b2", null, CancellationToken.None);

        // Assert
        result.IsOk.Should().BeTrue();
        lead.Status.Should().Be(LeadStatus.Approved);
        await repo.Received(1).UpdateAsync(lead, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptAsync_ForAnotherCustomersLead_ReturnsNotFound_AndDoesNotMutate()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var attackerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(ownerId));
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var sut = new LeadWorkflow(repo);

        // Act
        var result = await sut.AcceptAsync(attackerId, lead.Id.Value, "s", "b", null, CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.NotFound);
        lead.Status.Should().Be(LeadStatus.PendingReview);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Lead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptAsync_WithInvalidRecipient_ReturnsInvalid_AndDoesNotMutate()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var sut = new LeadWorkflow(repo);

        // Act
        var result = await sut.AcceptAsync(customerId, lead.Id.Value, "s", "b", "not-an-email", CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.Invalid);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Lead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkSentAsync_WithoutRecipient_ReturnsInvalid()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        lead.Approve();
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var sut = new LeadWorkflow(repo);

        // Act
        var result = await sut.MarkSentAsync(customerId, lead.Id.Value, CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.Invalid);
    }

    [Fact]
    public async Task DiscardAsync_WhenMissing_ReturnsNotFound()
    {
        // Arrange
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns((Lead?)null);
        var sut = new LeadWorkflow(repo);

        // Act
        var result = await sut.DiscardAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.NotFound);
    }
}

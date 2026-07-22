using Coldstart.Application.Leads;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class GetLeadDetailTests
{
    private static Lead LeadFor(CustomerId customerId) => Lead.CreateHarvested(
        customerId, "Acme", Fit.Yes, "subj", "body",
        "hackernews", "https://x/1", "fp-1", 0.7, "outbound", DateTimeOffset.UtcNow);

    [Fact]
    public async Task ForLeadAsync_WhenOwned_ReturnsDetailWithContactLinks()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var sut = new GetLeadDetail(repo);

        // Act
        var detail = await sut.ForLeadAsync(customerId, lead.Id.Value, CancellationToken.None);

        // Assert
        detail.Should().NotBeNull();
        detail!.Company.Should().Be("Acme");
        detail.Subject.Should().Be("subj");
        detail.ContactLinks.Should().NotBeEmpty();
        detail.MatchedSignals.Should().Be("outbound");
        detail.EmailConfidence.Should().Be(EmailConfidence.None.ToString());
        detail.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task ForLeadAsync_WithNotes_MapsThemToNoteViews()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var noteId = lead.AddNote("call back Friday", DateTimeOffset.UtcNow);
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var sut = new GetLeadDetail(repo);

        // Act
        var detail = await sut.ForLeadAsync(customerId, lead.Id.Value, CancellationToken.None);

        // Assert
        detail!.Notes.Should().ContainSingle(n =>
            n.Id == noteId && n.Text == "call back Friday" && n.UpdatedAt == null);
    }

    [Fact]
    public async Task ForLeadAsync_WithMultipleNotes_OrdersThemNewestFirst()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var olderId = lead.AddNote("older note", DateTimeOffset.UtcNow.AddMinutes(-10));
        var newerId = lead.AddNote("newer note", DateTimeOffset.UtcNow);
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var sut = new GetLeadDetail(repo);

        // Act
        var detail = await sut.ForLeadAsync(customerId, lead.Id.Value, CancellationToken.None);

        // Assert
        detail!.Notes.Should().HaveCount(2);
        detail.Notes[0].Id.Should().Be(newerId);
        detail.Notes[1].Id.Should().Be(olderId);
    }

    [Fact]
    public async Task ForLeadAsync_ForAnotherCustomer_ReturnsNull()
    {
        // Arrange
        var lead = LeadFor(new CustomerId(Guid.NewGuid()));
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var sut = new GetLeadDetail(repo);

        // Act
        var detail = await sut.ForLeadAsync(Guid.NewGuid(), lead.Id.Value, CancellationToken.None);

        // Assert
        detail.Should().BeNull();
    }

    [Fact]
    public async Task ForLeadAsync_WhenMissing_ReturnsNull()
    {
        // Arrange
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns((Lead?)null);
        var sut = new GetLeadDetail(repo);

        // Act
        var detail = await sut.ForLeadAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        // Assert
        detail.Should().BeNull();
    }
}

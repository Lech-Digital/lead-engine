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
        var sut = new GetLeadDetail(repo, Substitute.For<ICustomerRepository>(), new QuotaOptions());

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
        var sut = new GetLeadDetail(repo, Substitute.For<ICustomerRepository>(), new QuotaOptions());

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
        var sut = new GetLeadDetail(repo, Substitute.For<ICustomerRepository>(), new QuotaOptions());

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
        var sut = new GetLeadDetail(repo, Substitute.For<ICustomerRepository>(), new QuotaOptions());

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
        var sut = new GetLeadDetail(repo, Substitute.For<ICustomerRepository>(), new QuotaOptions());

        // Act
        var detail = await sut.ForLeadAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        // Assert
        detail.Should().BeNull();
    }

    [Fact]
    public async Task ForLeadAsync_WhenLeadIsLockedForFreeCustomer_ReturnsNull()
    {
        // Arrange — Free customer, quota 1, two leads; the 2nd (lower score) is locked
        var cid = Guid.NewGuid();
        var customer = Customer.Create("Acme");   // Free
        var l1 = Lead.CreateHarvested(new CustomerId(cid), "A", Fit.Yes, "s", "b", "hn", "https://x/1", "fp1", 0.9, "x", DateTimeOffset.UtcNow);
        var l2 = Lead.CreateHarvested(new CustomerId(cid), "B", Fit.Yes, "s", "b", "hn", "https://x/2", "fp2", 0.5, "x", DateTimeOffset.UtcNow);
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(customer);
        var leads = Substitute.For<ILeadRepository>();
        leads.GetAsync(l2.Id, Arg.Any<CancellationToken>()).Returns(l2);
        leads.ForCustomerAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(new[] { l1, l2 });
        var sut = new GetLeadDetail(leads, customers, new QuotaOptions { FreeLeadCount = 1 });

        // Act
        var detail = await sut.ForLeadAsync(cid, l2.Id.Value, CancellationToken.None);

        // Assert
        detail.Should().BeNull();
    }

    [Fact]
    public async Task ForLeadAsync_WhenLeadIsUnlocked_ReturnsDetail()
    {
        // Arrange — the top lead under quota 1 is unlocked
        var cid = Guid.NewGuid();
        var customer = Customer.Create("Acme");
        var l1 = Lead.CreateHarvested(new CustomerId(cid), "A", Fit.Yes, "s", "b", "hn", "https://x/1", "fp1", 0.9, "x", DateTimeOffset.UtcNow);
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(customer);
        var leads = Substitute.For<ILeadRepository>();
        leads.GetAsync(l1.Id, Arg.Any<CancellationToken>()).Returns(l1);
        leads.ForCustomerAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(new[] { l1 });
        var sut = new GetLeadDetail(leads, customers, new QuotaOptions { FreeLeadCount = 1 });

        // Act
        var detail = await sut.ForLeadAsync(cid, l1.Id.Value, CancellationToken.None);

        // Assert
        detail.Should().NotBeNull();
    }
}

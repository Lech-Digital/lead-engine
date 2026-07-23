using Coldstart.Application.Leads;
using Coldstart.Application.Outreach;
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

    private static (ICustomerRepository customers, Customer customer) FreeCustomer(Guid id)
    {
        var customer = Customer.Create("Acme");   // Free by default
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(new CustomerId(id), Arg.Any<CancellationToken>()).Returns(customer);
        return (customers, customer);
    }

    [Fact]
    public async Task AcceptAsync_WithoutRecipient_ApprovesAndPersists()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var (customers, _) = FreeCustomer(customerId);
        var sut = new LeadWorkflow(repo, Substitute.For<IOutreachMailer>(), customers, new QuotaOptions());

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
        var (customers, _) = FreeCustomer(ownerId);
        var sut = new LeadWorkflow(repo, Substitute.For<IOutreachMailer>(), customers, new QuotaOptions());

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
        var (customers, _) = FreeCustomer(customerId);
        var sut = new LeadWorkflow(repo, Substitute.For<IOutreachMailer>(), customers, new QuotaOptions());

        // Act
        var result = await sut.AcceptAsync(customerId, lead.Id.Value, "s", "b", "not-an-email", CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.Invalid);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Lead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveAsync_KeepsExistingDraft()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var (customers, _) = FreeCustomer(customerId);
        var sut = new LeadWorkflow(repo, Substitute.For<IOutreachMailer>(), customers, new QuotaOptions());

        // Act
        var result = await sut.ApproveAsync(customerId, lead.Id.Value, CancellationToken.None);

        // Assert
        result.IsOk.Should().BeTrue();
        lead.Status.Should().Be(LeadStatus.Approved);
        lead.Subject.Should().Be("subj");
        lead.Draft.Should().Be("body");
        await repo.Received(1).UpdateAsync(lead, Arg.Any<CancellationToken>());
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
        var (customers, _) = FreeCustomer(customerId);
        var sut = new LeadWorkflow(repo, Substitute.For<IOutreachMailer>(), customers, new QuotaOptions());

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
        var (customers, _) = FreeCustomer(Guid.NewGuid());
        var sut = new LeadWorkflow(repo, Substitute.For<IOutreachMailer>(), customers, new QuotaOptions());

        // Act
        var result = await sut.DiscardAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.NotFound);
    }

    [Fact]
    public async Task AddNoteAsync_ForOwnedLead_PersistsNote()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var (customers, _) = FreeCustomer(customerId);
        var sut = new LeadWorkflow(repo, Substitute.For<IOutreachMailer>(), customers, new QuotaOptions());

        // Act
        var noteId = Guid.NewGuid();
        var result = await sut.AddNoteAsync(customerId, lead.Id.Value, noteId, "ring them Tuesday", CancellationToken.None);

        // Assert
        result.IsOk.Should().BeTrue();
        (await repo.GetAsync(new LeadId(lead.Id.Value), CancellationToken.None))!.Notes
            .Should().ContainSingle(n => n.Id == noteId && n.Text == "ring them Tuesday");
        await repo.Received(1).UpdateAsync(lead, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddNoteAsync_WithBlankText_ReturnsInvalid()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var (customers, _) = FreeCustomer(customerId);
        var sut = new LeadWorkflow(repo, Substitute.For<IOutreachMailer>(), customers, new QuotaOptions());

        // Act
        var result = await sut.AddNoteAsync(customerId, lead.Id.Value, Guid.NewGuid(), "   ", CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.Invalid);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Lead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddNoteAsync_ForAnotherCustomersLead_ReturnsNotFound()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var (customers, _) = FreeCustomer(customerId);
        var sut = new LeadWorkflow(repo, Substitute.For<IOutreachMailer>(), customers, new QuotaOptions());

        // Act
        var result = await sut.AddNoteAsync(Guid.NewGuid(), lead.Id.Value, Guid.NewGuid(), "x", CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.NotFound);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Lead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateNoteAsync_ForOwnedLead_UpdatesNoteText()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var noteId = lead.AddNote("original", DateTimeOffset.UtcNow);
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var (customers, _) = FreeCustomer(customerId);
        var sut = new LeadWorkflow(repo, Substitute.For<IOutreachMailer>(), customers, new QuotaOptions());

        // Act
        var result = await sut.UpdateNoteAsync(customerId, lead.Id.Value, noteId, "revised", CancellationToken.None);

        // Assert
        result.IsOk.Should().BeTrue();
        lead.Notes.Should().ContainSingle(n => n.Id == noteId && n.Text == "revised" && n.UpdatedAt != null);
        await repo.Received(1).UpdateAsync(lead, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateNoteAsync_WithBlankText_ReturnsInvalid()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var noteId = lead.AddNote("original", DateTimeOffset.UtcNow);
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var (customers, _) = FreeCustomer(customerId);
        var sut = new LeadWorkflow(repo, Substitute.For<IOutreachMailer>(), customers, new QuotaOptions());

        // Act
        var result = await sut.UpdateNoteAsync(customerId, lead.Id.Value, noteId, "   ", CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.Invalid);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Lead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveNoteAsync_RemovesTheNote()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var (customers, _) = FreeCustomer(customerId);
        var sut = new LeadWorkflow(repo, Substitute.For<IOutreachMailer>(), customers, new QuotaOptions());
        var noteId = Guid.NewGuid();
        await sut.AddNoteAsync(customerId, lead.Id.Value, noteId, "temp", CancellationToken.None);

        // Act
        var result = await sut.RemoveNoteAsync(customerId, lead.Id.Value, noteId, CancellationToken.None);

        // Assert
        result.IsOk.Should().BeTrue();
        (await repo.GetAsync(new LeadId(lead.Id.Value), CancellationToken.None))!.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveNoteAsync_ForAnotherCustomersLead_ReturnsNotFound()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var noteId = lead.AddNote("temp", DateTimeOffset.UtcNow);
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var (customers, _) = FreeCustomer(customerId);
        var sut = new LeadWorkflow(repo, Substitute.For<IOutreachMailer>(), customers, new QuotaOptions());

        // Act
        var result = await sut.RemoveNoteAsync(Guid.NewGuid(), lead.Id.Value, noteId, CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.NotFound);
        lead.Notes.Should().ContainSingle(n => n.Id == noteId);
    }

    [Fact]
    public async Task ApproveAsync_WhenLeadIsLocked_ReturnsNotFound()
    {
        // Arrange — Free customer, quota 1; act on the locked (2nd) lead
        var cid = Guid.NewGuid();
        var (customers, _) = FreeCustomer(cid);
        var l1 = Lead.CreateHarvested(new CustomerId(cid), "A", Fit.Yes, "s", "b", "hn", "https://x/1", "fp1", 0.9, "x", DateTimeOffset.UtcNow);
        var l2 = Lead.CreateHarvested(new CustomerId(cid), "B", Fit.Yes, "s", "b", "hn", "https://x/2", "fp2", 0.5, "x", DateTimeOffset.UtcNow);
        var leads = Substitute.For<ILeadRepository>();
        leads.GetAsync(l2.Id, Arg.Any<CancellationToken>()).Returns(l2);
        leads.ForCustomerAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(new[] { l1, l2 });
        var outreach = Substitute.For<Coldstart.Application.Outreach.IOutreachMailer>();
        var sut = new LeadWorkflow(leads, outreach, customers, new QuotaOptions { FreeLeadCount = 1 });

        // Act
        var result = await sut.ApproveAsync(cid, l2.Id.Value, CancellationToken.None);

        // Assert — refused, and never persisted
        result.Status.Should().Be(LeadActionStatus.NotFound);
        await leads.DidNotReceive().UpdateAsync(Arg.Any<Lead>(), Arg.Any<CancellationToken>());
    }
}

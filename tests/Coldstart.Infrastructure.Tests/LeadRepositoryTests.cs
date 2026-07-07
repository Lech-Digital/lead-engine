using Coldstart.Domain;
using Coldstart.Infrastructure.Repositories;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class LeadRepositoryTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task AddAndReadBack_RoundTripsValueObjectsAndEnums()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        var customer = Customer.Create("Coldstart");
        var lead = customer.AddLead("Patronus AI", Fit.Yes, "Subj", "Body", DateTimeOffset.UtcNow);
        lead.Approve(new EmailAddress("anand@patronus.ai"));
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        // Act
        await using var readDb = fixture.NewDbContext();
        var repo = new LeadRepository(readDb);
        var result = await repo.ForCustomerAsync(customer.Id, CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].Company.Should().Be("Patronus AI");
        result[0].Status.Should().Be(LeadStatus.Approved);
        result[0].RecipientEmail!.Value.Should().Be("anand@patronus.ai");
        result[0].EmailConfidence.Should().Be(EmailConfidence.None);
    }

    [Fact]
    public async Task ForCustomerAsync_WhenCustomerHasNoLeads_ShouldReturnEmpty()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        var repo = new LeadRepository(db);
        var customerIdWithNoLeads = CustomerId.New();

        // Act
        var result = await repo.ForCustomerAsync(customerIdWithNoLeads, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_WhenLeadHasNoRecipientEmail_ShouldRoundTripAsPendingReviewWithNoConfidence()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        var customer = Customer.Create("Nimbus Data");
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var lead = Lead.Create(customer.Id, "Nimbus Data", Fit.Maybe, "Subj", "Body", DateTimeOffset.UtcNow);
        var repo = new LeadRepository(db);

        // Act
        await repo.AddAsync(lead, CancellationToken.None);
        await using var readDb = fixture.NewDbContext();
        var readRepo = new LeadRepository(readDb);
        var result = await readRepo.ForCustomerAsync(customer.Id, CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].Status.Should().Be(LeadStatus.PendingReview);
        result[0].RecipientEmail.Should().BeNull();
        result[0].EmailConfidence.Should().Be(EmailConfidence.None);
    }

    [Fact]
    public async Task ExistsByFingerprintAsync_TrueOnlyAfterSaved()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        var repo = new LeadRepository(db);
        var customer = Customer.Create("Coldstart");
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var lead = Lead.CreateHarvested(customer.Id, "Acme", Fit.Yes, "s", "b",
            "funding", "https://x/y", "fp-unique-1", 0.5, "raised", DateTimeOffset.UtcNow);

        // Act
        var before = await repo.ExistsByFingerprintAsync("fp-unique-1", CancellationToken.None);
        await repo.AddAsync(lead, CancellationToken.None);
        var after = await repo.ExistsByFingerprintAsync("fp-unique-1", CancellationToken.None);

        // Assert
        before.Should().BeFalse();
        after.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_ThenUpdate_PersistsStatusChange()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        var customer = Customer.Create("repo-update@test.com");
        await new CustomerRepository(db).AddAsync(customer, CancellationToken.None);
        var lead = Lead.CreateHarvested(customer.Id, "Acme", Fit.Yes, "s", "b",
            "hackernews", "https://x/1", "fp-upd", 0.7, "outbound", DateTimeOffset.UtcNow);
        var repo = new LeadRepository(db);
        await repo.AddAsync(lead, CancellationToken.None);

        // Act
        var loaded = await repo.GetAsync(lead.Id, CancellationToken.None);
        loaded!.Approve(new EmailAddress("ceo@acme.com"));
        await repo.UpdateAsync(loaded, CancellationToken.None);

        // Assert
        await using var read = fixture.NewDbContext();
        var reloaded = await new LeadRepository(read).GetAsync(lead.Id, CancellationToken.None);
        reloaded!.Status.Should().Be(LeadStatus.Approved);
        reloaded.RecipientEmail!.Value.Should().Be("ceo@acme.com");
    }

    [Fact]
    public async Task GetAsync_WhenMissing_ReturnsNull()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        var repo = new LeadRepository(db);

        // Act
        var loaded = await repo.GetAsync(LeadId.New(), CancellationToken.None);

        // Assert
        loaded.Should().BeNull();
    }
}

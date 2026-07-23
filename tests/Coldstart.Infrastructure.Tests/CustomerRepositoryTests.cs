using Coldstart.Domain;
using Coldstart.Infrastructure.Repositories;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class CustomerRepositoryTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task AddAsync_PersistsCustomer_AndAllAsyncReturnsIt()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        var repo = new CustomerRepository(db);
        var customer = Customer.Create("Acme Robotics");

        // Act
        await repo.AddAsync(customer, CancellationToken.None);
        await using var readDb = fixture.NewDbContext();
        var readRepo = new CustomerRepository(readDb);
        var all = await readRepo.AllAsync(CancellationToken.None);

        // Assert
        all.Should().Contain(c => c.Id == customer.Id && c.Name == "Acme Robotics");
    }

    [Fact]
    public async Task GetByNameAsync_ReturnsSavedCustomer_OrNull()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        var repo = new CustomerRepository(db);
        var customer = Customer.Create("sam@acme.com");
        await repo.AddAsync(customer, CancellationToken.None);

        // Act
        var found = await repo.GetByNameAsync("sam@acme.com", CancellationToken.None);
        var missing = await repo.GetByNameAsync("nobody@nowhere.com", CancellationToken.None);

        // Assert
        found.Should().NotBeNull();
        found!.Id.Should().Be(customer.Id);
        missing.Should().BeNull();
    }

    [Fact]
    public async Task Profile_RoundTripsThroughOwnedType()
    {
        // Arrange
        var customer = Customer.Create("profile-roundtrip@acme.com");
        await using var write = fixture.NewDbContext(customer.Id.Value);
        customer.UpdateProfile(new CompanyProfile("Acme Ltd", "1 Main St", "NL123", "KVK9", "B2B SaaS", "acme.com", "hi@acme.com"));
        new CustomerRepository(write);   // (repo uses db directly)
        write.Customers.Add(customer);
        await write.SaveChangesAsync();

        // Act
        await using var read = fixture.NewDbContext(customer.Id.Value);
        var found = await new CustomerRepository(read).GetAsync(customer.Id, CancellationToken.None);

        // Assert
        found.Should().NotBeNull();
        found!.Profile.CompanyName.Should().Be("Acme Ltd");
        found.Profile.VatNumber.Should().Be("NL123");
        found.Profile.ContactEmail.Should().Be("hi@acme.com");
    }

    [Fact]
    public async Task DeleteAsync_RemovesCustomerAndItsLeads()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        var customers = new CustomerRepository(db);
        var leads = new LeadRepository(db);
        var customer = Customer.Create("delete-me@test.com");
        await customers.AddAsync(customer, CancellationToken.None);
        var lead = customer.AddLead("Lead Co", Fit.Yes, "subj", "draft", DateTimeOffset.UtcNow);
        await leads.AddAsync(lead, CancellationToken.None);

        // Act
        await customers.DeleteAsync(customer.Id, CancellationToken.None);

        // Assert — read back scoped to the deleted tenant, so this proves the rows are actually gone
        // (not merely filtered out by a mismatched tenant context).
        await using var readDb = fixture.NewDbContext(customer.Id.Value);
        var readCustomers = new CustomerRepository(readDb);
        var readLeads = new LeadRepository(readDb);
        (await readCustomers.GetAsync(customer.Id, CancellationToken.None)).Should().BeNull();
        (await readLeads.ForCustomerAsync(customer.Id, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_AfterSavingBriefAndToggle_RoundTripsThem()
    {
        // Arrange
        var customer = Customer.Create("brief-co@example.com");
        await using var write = fixture.NewDbContext(customer.Id.Value);
        customer.UpdateProfile(new CompanyProfile("Acme", null, null, null, null, null, null,
            Brief: "We do X for Y.", MentionSourceTool: true));
        new CustomerRepository(write);
        write.Customers.Add(customer);
        await write.SaveChangesAsync();

        // Act — fresh context to force a real read
        await using var read = fixture.NewDbContext(customer.Id.Value);
        var reloaded = await new CustomerRepository(read).GetAsync(customer.Id, CancellationToken.None);

        // Assert
        reloaded!.Profile.Brief.Should().Be("We do X for Y.");
        reloaded.Profile.MentionSourceTool.Should().BeTrue();
    }

    [Fact]
    public async Task Update_PersistsPlanHarvestStatusAndSearchProfile()
    {
        // Arrange — provision a customer, then set plan + harvest + a generated search profile
        var name = $"Cust-{Guid.NewGuid():N}";
        Guid id;
        await using (var seed = fixture.NewDbContext())
        {
            var c = Customer.Create(name);
            seed.Customers.Add(c);
            await seed.SaveChangesAsync();
            id = c.Id.Value;
        }
        await using (var write = fixture.NewDbContext(/* tenant */ null))
        {
            var repo = new CustomerRepository(write);
            var c = await repo.GetByNameAsync(name, CancellationToken.None);
            c!.SetPlan(CustomerPlan.Full);
            c.MarkHarvest(HarvestStatus.Done, DateTimeOffset.UtcNow);
            c.UpdateProfile(new CompanyProfile("Co", null, null, null, "dentists", null, null,
                Brief: "we sell scheduling", MentionSourceTool: false,
                Offer: "scheduling software", TargetKeywords: "dentist\nclinic",
                BuyingSignals: "hiring\nexpanding", Disqualifiers: "enterprise"));
            await repo.UpdateAsync(c, CancellationToken.None);
        }

        // Act — read it back (GetByName bypasses the tenant filter)
        await using var read = fixture.NewDbContext();
        var loaded = await new CustomerRepository(read).GetByNameAsync(name, CancellationToken.None);

        // Assert
        loaded!.Plan.Should().Be(CustomerPlan.Full);
        loaded.HarvestStatus.Should().Be(HarvestStatus.Done);
        loaded.Profile.TargetKeywords.Should().Be("dentist\nclinic");
        loaded.Profile.Offer.Should().Be("scheduling software");
    }
}

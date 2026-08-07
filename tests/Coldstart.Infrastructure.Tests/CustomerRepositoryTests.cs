using Coldstart.Application.Ports;
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
    public async Task Targeting_RoundTripsThroughOwnedType()
    {
        // Arrange
        var customer = Customer.Create("targeting-roundtrip@acme.com");
        await using var write = fixture.NewDbContext(customer.Id.Value);
        customer.UpdateTargeting(Targeting.Create(
            industries: ["Dentists", "Clinics"],
            geographies: ["NL", "BE"],
            buyerRoles: ["Owner", "Office Manager"],
            sizeMin: 5,
            sizeMax: 50,
            triggerEvents: ["New location opened"],
            exclusions: ["Enterprise chains"]));
        write.Customers.Add(customer);
        await write.SaveChangesAsync();

        // Act
        await using var read = fixture.NewDbContext(customer.Id.Value);
        var found = await new CustomerRepository(read).GetAsync(customer.Id, CancellationToken.None);

        // Assert
        found.Should().NotBeNull();
        found!.Targeting.IndustryList.Should().Equal("Dentists", "Clinics");
        found.Targeting.GeographyList.Should().Equal("NL", "BE");
        found.Targeting.BuyerRoleList.Should().Equal("Owner", "Office Manager");
        found.Targeting.SizeMin.Should().Be(5);
        found.Targeting.SizeMax.Should().Be(50);
        found.Targeting.TriggerEventList.Should().Equal("New location opened");
        found.Targeting.ExclusionList.Should().Equal("Enterprise chains");
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

    [Fact]
    public async Task LeadCountsAsync_WithLeadsAcrossCustomersAndStatuses_ShouldGroupPerCustomer()
    {
        // Arrange - create customers and leads
        var now = DateTimeOffset.UtcNow;
        Guid idA, idB;

        await using (var db = fixture.NewDbContext())
        {
            var customers = new CustomerRepository(db);
            var leads = new LeadRepository(db);

            // Add customer A
            var a = Customer.Create($"counts-a-{Guid.NewGuid()}");
            idA = a.Id.Value;
            db.Customers.Add(a);
            await db.SaveChangesAsync();

            // Add leads for customer A
            var lead1 = Lead.Create(a.Id, "Acme", Fit.Yes, "s1", "d1", now);
            var lead2 = Lead.Create(a.Id, "Beta", Fit.Yes, "s2", "d2", now);
            var lead3 = Lead.Create(a.Id, "Gamma", Fit.Maybe, "s3", "d3", now);
            lead3.Approve();
            await leads.AddAsync(lead1, CancellationToken.None);
            await leads.AddAsync(lead2, CancellationToken.None);
            await leads.AddAsync(lead3, CancellationToken.None);
        }

        await using (var db = fixture.NewDbContext())
        {
            var customers = new CustomerRepository(db);
            var leads = new LeadRepository(db);

            // Add customer B
            var b = Customer.Create($"counts-b-{Guid.NewGuid()}");
            idB = b.Id.Value;
            db.Customers.Add(b);
            await db.SaveChangesAsync();

            // Add lead for customer B
            var lead4 = Lead.Create(b.Id, "Delta", Fit.Yes, "s4", "d4", now);
            await leads.AddAsync(lead4, CancellationToken.None);
        }

        // Act
        await using var readDb = fixture.NewDbContext();
        var counts = await new CustomerRepository(readDb).LeadCountsAsync(CancellationToken.None);

        // Assert
        counts[idA].Should().Be(new LeadCounts(Total: 3, PendingReview: 2));
        counts[idB].Should().Be(new LeadCounts(Total: 1, PendingReview: 1));
    }

    [Fact]
    public async Task LeadCountsAsync_CustomerWithoutLeads_ShouldBeAbsentFromResult()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        var repo = new CustomerRepository(db);
        var empty = Customer.Create($"counts-empty-{Guid.NewGuid()}");
        await repo.AddAsync(empty, CancellationToken.None);

        // Act
        var counts = await repo.LeadCountsAsync(CancellationToken.None);

        // Assert
        counts.Should().NotContainKey(empty.Id.Value);
    }
}

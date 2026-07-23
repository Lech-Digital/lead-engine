using Coldstart.Domain;
using Coldstart.Infrastructure.Repositories;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class TenantIsolationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Lead_IsNotVisible_ToAnotherTenant()
    {
        // Arrange — two customers, one lead each (distinct fingerprints: composite index lands in Task 5)
        Guid customerA, customerB, leadA;
        await using (var seed = fixture.NewDbContext())
        {
            var a = Customer.Create($"A-{Guid.NewGuid():N}");
            var b = Customer.Create($"B-{Guid.NewGuid():N}");
            var la = a.AddLead("Acme", Fit.Yes, "s", "b", DateTimeOffset.UtcNow);
            var lb = b.AddLead("Globex", Fit.Yes, "s", "b", DateTimeOffset.UtcNow);
            seed.Customers.AddRange(a, b);
            await seed.SaveChangesAsync();
            customerA = a.Id.Value; customerB = b.Id.Value; leadA = la.Id.Value;
        }

        // Act — read as tenant B
        await using var asB = fixture.NewDbContext(customerB);
        var repoB = new LeadRepository(asB);
        var crossRead = await repoB.GetAsync(new LeadId(leadA), CancellationToken.None);
        var ownRead = await repoB.ForCustomerAsync(new CustomerId(customerA), CancellationToken.None);

        // Assert — B cannot see A's lead by id, and a scoped list for A returns nothing under B's context
        crossRead.Should().BeNull();
        ownRead.Should().BeEmpty();

        // And A sees its own lead
        await using var asA = fixture.NewDbContext(customerA);
        (await new LeadRepository(asA).GetAsync(new LeadId(leadA), CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task Customer_IsNotVisible_ToAnotherTenant_ButAdminBypassSeesAll()
    {
        // Arrange
        Guid a, b;
        await using (var seed = fixture.NewDbContext())
        {
            var ca = Customer.Create($"A-{Guid.NewGuid():N}");
            var cb = Customer.Create($"B-{Guid.NewGuid():N}");
            seed.Customers.AddRange(ca, cb);
            await seed.SaveChangesAsync();
            a = ca.Id.Value; b = cb.Id.Value;
        }

        // Act / Assert — as tenant A, GetAsync(B) is filtered out; admin AllAsync (IgnoreQueryFilters) sees both
        await using var asA = fixture.NewDbContext(a);
        var repoA = new CustomerRepository(asA);
        (await repoA.GetAsync(new CustomerId(b), CancellationToken.None)).Should().BeNull();
        (await repoA.GetAsync(new CustomerId(a), CancellationToken.None)).Should().NotBeNull();
        var all = await repoA.AllAsync(CancellationToken.None);
        all.Select(c => c.Id.Value).Should().Contain(new[] { a, b });
    }

    [Fact]
    public async Task GetByName_FindsCustomer_WithNoTenantSet()
    {
        // Arrange — provisioning path: no tenant established yet
        var name = $"Prov-{Guid.NewGuid():N}";
        await using (var seed = fixture.NewDbContext())
        {
            seed.Customers.Add(Customer.Create(name));
            await seed.SaveChangesAsync();
        }

        // Act
        await using var noTenant = fixture.NewDbContext();
        var found = await new CustomerRepository(noTenant).GetByNameAsync(name, CancellationToken.None);

        // Assert
        found.Should().NotBeNull();
    }
}

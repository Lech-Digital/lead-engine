using Coldstart.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class DbSeederTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task SeedAsync_OnEmptyDb_CreatesDemoCustomerWithLeads()
    {
        // Arrange
        await using var db = fixture.NewDbContext();

        // Act
        await DbSeeder.SeedAsync(db, CancellationToken.None);

        // Assert — a system-level check of seed state, not a tenant-scoped read; bypass the filter
        // deliberately (there is no signed-in tenant at boot, mirroring the seeder itself).
        var customers = await db.Customers.IgnoreQueryFilters().Include(c => c.Leads).ToListAsync();
        customers.Should().ContainSingle();
        customers[0].Leads.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SeedAsync_WhenAlreadySeeded_IsIdempotent()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        await DbSeeder.SeedAsync(db, CancellationToken.None);

        // Act
        await DbSeeder.SeedAsync(db, CancellationToken.None);

        // Assert — bypass the filter for the same reason as above.
        (await db.Customers.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }
}

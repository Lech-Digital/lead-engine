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

        // Assert
        var customers = await db.Customers.Include(c => c.Leads).ToListAsync();
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

        // Assert
        (await db.Customers.CountAsync()).Should().Be(1);
    }
}

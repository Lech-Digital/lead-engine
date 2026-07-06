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
}

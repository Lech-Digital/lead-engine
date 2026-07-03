using Coldstart.Application.Customers;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class ProvisionCustomerTests
{
    [Fact]
    public async Task ProvisionAsync_CreatesCustomerWithSeededLeads_AndReturnsId()
    {
        // Arrange
        var customers = Substitute.For<ICustomerRepository>();
        Customer? saved = null;
        await customers.AddAsync(Arg.Do<Customer>(c => saved = c), Arg.Any<CancellationToken>());
        var sut = new ProvisionCustomer(customers);

        // Act
        var id = await sut.ProvisionAsync("sam@acme.com", CancellationToken.None);

        // Assert
        saved.Should().NotBeNull();
        saved!.Leads.Should().NotBeEmpty();       // mock leads seeded
        id.Should().Be(saved.Id.Value);
    }
}

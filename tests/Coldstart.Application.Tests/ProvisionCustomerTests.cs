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
    public async Task ProvisionAsync_WhenNoExistingCustomer_CreatesOneWithNoLeads()
    {
        // Arrange
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetByNameAsync("sam@acme.com", Arg.Any<CancellationToken>()).Returns((Customer?)null);
        Customer? saved = null;
        await customers.AddAsync(Arg.Do<Customer>(c => saved = c), Arg.Any<CancellationToken>());
        var sut = new ProvisionCustomer(customers);

        // Act
        var id = await sut.ProvisionAsync("sam@acme.com", CancellationToken.None);

        // Assert
        saved.Should().NotBeNull();
        saved!.Leads.Should().BeEmpty();       // no mock seeding
        id.Should().Be(saved.Id.Value);
    }

    [Fact]
    public async Task ProvisionAsync_WhenCustomerWithNameExists_LinksToItWithoutCreating()
    {
        // Arrange
        var customers = Substitute.For<ICustomerRepository>();
        var existing = Customer.Create("sam@acme.com");
        customers.GetByNameAsync("sam@acme.com", Arg.Any<CancellationToken>()).Returns(existing);
        var sut = new ProvisionCustomer(customers);

        // Act
        var id = await sut.ProvisionAsync("sam@acme.com", CancellationToken.None);

        // Assert
        id.Should().Be(existing.Id.Value);
        await customers.DidNotReceive().AddAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }
}

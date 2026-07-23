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

    [Fact]
    public async Task ProvisionAsync_WithSeed_StoresProfileAndMarksHarvestPending()
    {
        // Arrange
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetByNameAsync("Acme", Arg.Any<CancellationToken>()).Returns((Customer?)null);
        Customer? saved = null;
        await customers.AddAsync(Arg.Do<Customer>(c => saved = c), Arg.Any<CancellationToken>());
        var sut = new ProvisionCustomer(customers);
        var seed = new CompanyProfileSeed("dental clinics", "we sell scheduling",
            new SearchProfile("scheduling", "dentist\nclinic", "hiring", "labs"));

        // Act
        await sut.ProvisionAsync("Acme", seed, CancellationToken.None);

        // Assert
        saved.Should().NotBeNull();
        saved!.Profile.Niche.Should().Be("dental clinics");
        saved.Profile.Brief.Should().Be("we sell scheduling");
        saved.Profile.TargetKeywords.Should().Be("dentist\nclinic");
        saved.Profile.Offer.Should().Be("scheduling");
        saved.HarvestStatus.Should().Be(HarvestStatus.Pending);
    }

    [Fact]
    public async Task ProvisionAsync_WithSeed_WhenNameExists_ReturnsExistingWithoutOverwriting()
    {
        // Arrange
        var existing = Customer.Create("Acme");
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetByNameAsync("Acme", Arg.Any<CancellationToken>()).Returns(existing);
        var sut = new ProvisionCustomer(customers);

        // Act
        var id = await sut.ProvisionAsync("Acme", new CompanyProfileSeed("x", "y", null), CancellationToken.None);

        // Assert
        id.Should().Be(existing.Id.Value);
        await customers.DidNotReceive().AddAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }
}

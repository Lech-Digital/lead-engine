using Coldstart.Application.Customers;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class CompanyProfileUseCasesTests
{
    [Fact]
    public async Task Update_LoadsCustomer_AppliesProfile_Saves()
    {
        // Arrange
        var customer = Customer.Create("sam@acme.com");
        var repo = Substitute.For<ICustomerRepository>();
        repo.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new UpdateCompanyProfile(repo);
        var input = new CompanyProfileInput("Acme Ltd", "1 Main St", "NL1", "KVK1", "B2B", "acme.com", "hi@acme.com");

        // Act
        await sut.UpdateAsync(customer.Id.Value, input, CancellationToken.None);

        // Assert
        customer.Profile.CompanyName.Should().Be("Acme Ltd");
        await repo.Received(1).UpdateAsync(customer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_WhenCustomerMissing_DoesNothing()
    {
        // Arrange
        var repo = Substitute.For<ICustomerRepository>();
        repo.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        var sut = new UpdateCompanyProfile(repo);

        // Act
        var act = async () => await sut.UpdateAsync(Guid.NewGuid(), new CompanyProfileInput(null, null, null, null, null, null, null), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_MapsProfileToView_AndReportsEmpty()
    {
        // Arrange
        var customer = Customer.Create("sam@acme.com");
        var repo = Substitute.For<ICustomerRepository>();
        repo.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new GetCompanyProfile(repo);

        // Act
        var view = await sut.ForCustomerAsync(customer.Id.Value, CancellationToken.None);

        // Assert
        view.IsEmpty.Should().BeTrue();
        view.CompanyName.Should().BeNull();
    }
}

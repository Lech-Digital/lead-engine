using Coldstart.Application.Customers;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class TargetingUseCasesTests
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();

    [Fact]
    public async Task UpdateAsync_MultilineInput_StoresNormalisedTargeting()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        _customers.GetAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new UpdateTargeting(_customers);
        var input = new TargetingInput("logistics\n 3PL \nlogistics", "NL\nBE", "Fleet Manager", 10, 200,
            "raised funding", "competitors");

        // Act
        var result = await sut.UpdateAsync(customer.Id.Value, input, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        customer.Targeting.IndustryList.Should().Equal("logistics", "3PL");
        customer.Targeting.GeographyList.Should().Equal("NL", "BE");
        await _customers.Received(1).UpdateAsync(customer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_InvertedSizeBand_ReturnsFieldErrorAndPersistsNothing()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        _customers.GetAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new UpdateTargeting(_customers);
        var input = new TargetingInput(null, null, null, 200, 10, null, null);

        // Act
        var result = await sut.UpdateAsync(customer.Id.Value, input, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("smallest company size");
        await _customers.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_UnknownCustomer_ReturnsNotFoundAndPersistsNothing()
    {
        // Arrange
        _customers.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        var sut = new UpdateTargeting(_customers);

        // Act
        var result = await sut.UpdateAsync(Guid.NewGuid(), new TargetingInput(null, null, null, null, null, null, null),
            CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        await _customers.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_AllFieldsCleared_IsAllowedAndLeavesEmptyTargeting()
    {
        // Arrange — clearing the form must be legal; the harvest falls back to legacy fields
        var customer = Customer.Create("Acme");
        customer.UpdateTargeting(Targeting.Create(["logistics"], [], [], null, null, [], []));
        _customers.GetAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new UpdateTargeting(_customers);

        // Act
        var result = await sut.UpdateAsync(customer.Id.Value,
            new TargetingInput("", "", "", null, null, "", ""), CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        customer.Targeting.HasAny.Should().BeFalse();
    }

    [Fact]
    public async Task ForCustomerAsync_StoredTargeting_ReturnsTextForTextareas()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        customer.UpdateTargeting(Targeting.Create(["logistics", "3PL"], ["NL"], [], 10, null, [], []));
        _customers.GetAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new GetTargeting(_customers);

        // Act
        var view = await sut.ForCustomerAsync(customer.Id.Value, CancellationToken.None);

        // Assert
        view.Should().NotBeNull();
        view!.Industries.Should().Be("logistics\n3PL");
        view.BuyerRoles.Should().BeEmpty();
        view.SizeMin.Should().Be(10);
    }

    [Fact]
    public async Task ForCustomerAsync_UnknownCustomer_ReturnsNull()
    {
        // Arrange
        _customers.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        var sut = new GetTargeting(_customers);

        // Act
        var view = await sut.ForCustomerAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        view.Should().BeNull();
    }
}

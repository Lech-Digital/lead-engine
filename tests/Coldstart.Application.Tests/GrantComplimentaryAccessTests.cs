using Coldstart.Application.Billing;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class GrantComplimentaryAccessTests
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();

    [Fact]
    public async Task ExecuteAsync_FreeCustomer_GrantsAndPersists()
    {
        // Arrange
        var customer = Customer.Create("Independent");
        _customers.GetForSystemAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new GrantComplimentaryAccess(_customers);

        // Act
        var result = await sut.ExecuteAsync(customer.Id.Value, 5, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Until.Should().NotBeNull();
        customer.Plan.Should().Be(CustomerPlan.Full);
        await _customers.Received(1).UpdateAsync(customer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_UnknownCustomer_ReturnsNotFound()
    {
        // Arrange
        _customers.GetForSystemAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        var sut = new GrantComplimentaryAccess(_customers);

        // Act
        var result = await sut.ExecuteAsync(Guid.NewGuid(), 5, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("No such customer");
    }

    [Fact]
    public async Task ExecuteAsync_NonPositiveDays_ReturnsInvalid()
    {
        // Arrange
        var sut = new GrantComplimentaryAccess(_customers);

        // Act
        var result = await sut.ExecuteAsync(Guid.NewGuid(), 0, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("at least 1 day");
    }

    [Fact]
    public async Task ExecuteAsync_StripeSubscriber_ReturnsDomainGuardMessage()
    {
        // Arrange
        var customer = Customer.Create("Paying Co");
        customer.ActivateFull("sub_1", null);
        _customers.GetForSystemAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new GrantComplimentaryAccess(_customers);

        // Act
        var result = await sut.ExecuteAsync(customer.Id.Value, 5, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("already has a paid subscription");
        await _customers.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }
}

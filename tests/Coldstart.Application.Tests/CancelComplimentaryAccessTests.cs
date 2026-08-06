using Coldstart.Application.Billing;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class CancelComplimentaryAccessTests
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();

    [Fact]
    public async Task ExecuteAsync_ActiveGrant_CancelsAndPersists()
    {
        // Arrange
        var customer = Customer.Create("Comped Co");
        var now = DateTimeOffset.UtcNow;
        customer.GrantComplimentary(now.AddDays(5), now);
        _customers.GetForSystemAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new CancelComplimentaryAccess(_customers);

        // Act
        var result = await sut.ExecuteAsync(customer.Id.Value, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        customer.Plan.Should().Be(CustomerPlan.Free);
        customer.ComplimentaryUntil.Should().BeNull();
        await _customers.Received(1).UpdateAsync(customer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_UnknownCustomer_ReturnsNotFound()
    {
        // Arrange
        _customers.GetForSystemAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        var sut = new CancelComplimentaryAccess(_customers);

        // Act
        var result = await sut.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("No such customer");
    }

    [Fact]
    public async Task ExecuteAsync_NoActiveGrant_ReturnsDomainGuardMessage()
    {
        // Arrange — a Free customer that was never granted complimentary access
        var customer = Customer.Create("Never Comped Co");
        _customers.GetForSystemAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new CancelComplimentaryAccess(_customers);

        // Act
        var result = await sut.ExecuteAsync(customer.Id.Value, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("no active complimentary grant");
        await _customers.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }
}

using Coldstart.Application.Billing;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class ComplimentaryExpirySweeperTests
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Customer CompCustomer(string name, DateTimeOffset until)
    {
        var c = Customer.Create(name);
        c.GrantComplimentary(until, until.AddDays(-10));   // grant made 10 days before `until`
        return c;
    }

    [Fact]
    public async Task SweepAsync_LapsedTrial_DowngradesWithoutRetentionClock()
    {
        // Arrange
        var lapsed = CompCustomer("Lapsed Co", Now.AddDays(-1));
        _customers.AllAsync(Arg.Any<CancellationToken>()).Returns([lapsed]);
        var sut = new ComplimentaryExpirySweeper(_customers);

        // Act
        var count = await sut.SweepAsync(Now, CancellationToken.None);

        // Assert
        count.Should().Be(1);
        lapsed.Plan.Should().Be(CustomerPlan.Free);
        lapsed.ComplimentaryUntil.Should().BeNull();
        lapsed.SubscriptionEndedAt.Should().BeNull();
        await _customers.Received(1).UpdateAsync(lapsed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SweepAsync_ActiveTrial_LeavesCustomerAlone()
    {
        // Arrange
        var active = CompCustomer("Active Co", Now.AddDays(3));
        _customers.AllAsync(Arg.Any<CancellationToken>()).Returns([active]);
        var sut = new ComplimentaryExpirySweeper(_customers);

        // Act
        var count = await sut.SweepAsync(Now, CancellationToken.None);

        // Assert
        count.Should().Be(0);
        active.Plan.Should().Be(CustomerPlan.Full);
        await _customers.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SweepAsync_TrialConvertedToStripe_IsNotDowngraded()
    {
        // Arrange — subscribed mid-trial; ActivateFull cleared ComplimentaryUntil,
        // but defend against a lapsed date coexisting with a live subscription anyway.
        var converted = CompCustomer("Converted Co", Now.AddDays(-1));
        converted.ActivateFull("sub_9", Now.AddMonths(1));
        _customers.AllAsync(Arg.Any<CancellationToken>()).Returns([converted]);
        var sut = new ComplimentaryExpirySweeper(_customers);

        // Act
        var count = await sut.SweepAsync(Now, CancellationToken.None);

        // Assert
        count.Should().Be(0);
        converted.Plan.Should().Be(CustomerPlan.Full);
    }

    [Fact]
    public async Task SweepAsync_OneRowFails_OthersStillSwept()
    {
        // Arrange
        var bad = CompCustomer("Bad Co", Now.AddDays(-2));
        var good = CompCustomer("Good Co", Now.AddDays(-1));
        _customers.AllAsync(Arg.Any<CancellationToken>()).Returns([bad, good]);
        _customers.UpdateAsync(bad, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("db hiccup"));
        var sut = new ComplimentaryExpirySweeper(_customers);

        // Act
        var count = await sut.SweepAsync(Now, CancellationToken.None);

        // Assert — bad row is skipped, good row still downgraded
        count.Should().Be(1);
        good.Plan.Should().Be(CustomerPlan.Free);
    }
}

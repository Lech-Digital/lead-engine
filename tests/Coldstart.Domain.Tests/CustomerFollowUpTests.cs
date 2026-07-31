using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class CustomerFollowUpTests
{
    [Fact]
    public void MarkFollowUpSent_AdvancesStage()
    {
        // Arrange
        var customer = Customer.Create("Acme");

        // Act
        customer.MarkFollowUpSent(1);

        // Assert
        customer.FollowUpStage.Should().Be(1);
    }

    [Fact]
    public void MarkFollowUpSent_LowerOrRepeatedStage_NeverRewinds()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        customer.MarkFollowUpSent(2);

        // Act
        customer.MarkFollowUpSent(1);
        customer.MarkFollowUpSent(2);

        // Assert
        customer.FollowUpStage.Should().Be(2);
    }

    [Fact]
    public void OptOutOfFollowUps_SetsFlag_AndStartsFalse()
    {
        // Arrange
        var customer = Customer.Create("Acme");

        // Act & Assert
        customer.FollowUpsOptOut.Should().BeFalse();
        customer.OptOutOfFollowUps();
        customer.FollowUpsOptOut.Should().BeTrue();
    }
}

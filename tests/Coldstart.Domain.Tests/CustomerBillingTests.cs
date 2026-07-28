using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class CustomerBillingTests
{
    [Fact]
    public void ActivateFull_SetsPlanSubscriptionAndRenewal()
    {
        // Arrange
        var c = Customer.Create("Acme");
        var renews = DateTimeOffset.UtcNow.AddDays(30);

        // Act
        c.LinkStripeCustomer("cus_1");
        c.ActivateFull("sub_1", renews);

        // Assert
        c.Plan.Should().Be(CustomerPlan.Full);
        c.StripeCustomerId.Should().Be("cus_1");
        c.StripeSubscriptionId.Should().Be("sub_1");
        c.PlanRenewsAt.Should().Be(renews);
    }

    [Fact]
    public void DowngradeToFree_ClearsSubscription_ButKeepsStripeCustomer()
    {
        // Arrange
        var c = Customer.Create("Acme");
        c.LinkStripeCustomer("cus_1");
        c.ActivateFull("sub_1", DateTimeOffset.UtcNow.AddDays(30));

        // Act
        c.DowngradeToFree();

        // Assert
        c.Plan.Should().Be(CustomerPlan.Free);
        c.StripeSubscriptionId.Should().BeNull();
        c.PlanRenewsAt.Should().BeNull();
        c.StripeCustomerId.Should().Be("cus_1");   // kept so they can re-subscribe / reopen the Portal
    }

    [Fact]
    public void LinkStripeCustomer_IsIdempotent_AndDoesNotChangePlan()
    {
        // Arrange
        var c = Customer.Create("Acme");

        // Act
        c.LinkStripeCustomer("cus_1");
        c.LinkStripeCustomer("cus_1");

        // Assert
        c.StripeCustomerId.Should().Be("cus_1");
        c.Plan.Should().Be(CustomerPlan.Free);
    }
}

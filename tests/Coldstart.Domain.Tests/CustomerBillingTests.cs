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

    [Fact]
    public void GrantComplimentary_FreeCustomer_BecomesFullUntilDate()
    {
        // Arrange
        var customer = Customer.Create("Independent");
        var now = DateTimeOffset.UtcNow;
        var until = now.AddDays(5);

        // Act
        customer.GrantComplimentary(until, now);

        // Assert
        customer.Plan.Should().Be(CustomerPlan.Full);
        customer.ComplimentaryUntil.Should().Be(until);
        customer.SubscriptionEndedAt.Should().BeNull();
    }

    [Fact]
    public void GrantComplimentary_WithLiveStripeSubscription_Throws()
    {
        // Arrange
        var customer = Customer.Create("Paying Co");
        customer.ActivateFull("sub_123", DateTimeOffset.UtcNow.AddMonths(1));
        var now = DateTimeOffset.UtcNow;

        // Act
        Action act = () => customer.GrantComplimentary(now.AddDays(5), now);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("*already has a paid subscription*");
    }

    [Fact]
    public void GrantComplimentary_UntilInThePast_Throws()
    {
        // Arrange
        var customer = Customer.Create("Late Co");
        var now = DateTimeOffset.UtcNow;

        // Act
        Action act = () => customer.GrantComplimentary(now.AddDays(-1), now);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("*must be in the future*");
    }

    [Fact]
    public void ActivateFull_DuringComplimentaryTrial_ClearsComplimentaryUntil()
    {
        // Arrange
        var customer = Customer.Create("Converted Co");
        var now = DateTimeOffset.UtcNow;
        customer.GrantComplimentary(now.AddDays(5), now);

        // Act
        customer.ActivateFull("sub_456", now.AddMonths(1));

        // Assert
        customer.ComplimentaryUntil.Should().BeNull();
        customer.Plan.Should().Be(CustomerPlan.Full);
    }

    [Fact]
    public void DowngradeToFree_AfterComplimentaryTrial_ClearsTrialAndKeepsRetentionClockUnset()
    {
        // Arrange — a comp trial never paid, so the 60-day retention clock must not start
        var customer = Customer.Create("Lapsed Co");
        var now = DateTimeOffset.UtcNow;
        customer.GrantComplimentary(now.AddDays(5), now);

        // Act
        customer.DowngradeToFree(endedAt: null);

        // Assert
        customer.Plan.Should().Be(CustomerPlan.Free);
        customer.ComplimentaryUntil.Should().BeNull();
        customer.SubscriptionEndedAt.Should().BeNull();
    }

    [Fact]
    public void CancelComplimentary_ActiveGrant_ReturnsToFreeAndClearsUntil()
    {
        // Arrange
        var customer = Customer.Create("Comped Co");
        var now = DateTimeOffset.UtcNow;
        customer.GrantComplimentary(now.AddDays(5), now);

        // Act
        customer.CancelComplimentary();

        // Assert
        customer.Plan.Should().Be(CustomerPlan.Free);
        customer.ComplimentaryUntil.Should().BeNull();
    }

    [Fact]
    public void CancelComplimentary_NoActiveGrant_Throws()
    {
        // Arrange
        var customer = Customer.Create("Never Comped Co");

        // Act
        Action act = () => customer.CancelComplimentary();

        // Assert
        act.Should().Throw<DomainException>().WithMessage("*no active complimentary grant*");
    }

    [Fact]
    public void CancelComplimentary_ActiveGrant_DoesNotStartTheRetentionClock()
    {
        // Arrange — a comp trial never paid, so cancelling it must not touch the fields that
        // drive the privacy-policy §7 retention-deletion clock.
        var customer = Customer.Create("Comped Co");
        var now = DateTimeOffset.UtcNow;
        customer.GrantComplimentary(now.AddDays(5), now);

        // Act
        customer.CancelComplimentary();

        // Assert
        customer.SubscriptionEndedAt.Should().BeNull();
        customer.RetentionReminderSent.Should().BeFalse();
    }

    [Fact]
    public void GrantComplimentary_ToLapsedCustomer_StopsTheRetentionClock()
    {
        // Arrange — a customer who paid, lapsed, and was already reminded about deletion
        var customer = Customer.Create("Lapsed Then Comped Co");
        var now = DateTimeOffset.UtcNow;
        customer.ActivateFull("sub_789", now.AddMonths(1));
        customer.DowngradeToFree(now.AddDays(-54));
        customer.MarkRetentionReminderSent();

        // Act
        customer.GrantComplimentary(now.AddDays(30), now);

        // Assert — active access means the operator must not be told to delete their data
        customer.Plan.Should().Be(CustomerPlan.Full);
        customer.SubscriptionEndedAt.Should().BeNull();
        customer.RetentionReminderSent.Should().BeFalse();
    }
}

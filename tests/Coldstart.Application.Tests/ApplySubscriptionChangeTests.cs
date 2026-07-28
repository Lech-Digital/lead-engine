using Coldstart.Application.Billing;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class ApplySubscriptionChangeTests
{
    [Fact]
    public async Task Active_FirstActivation_LinksStripeCustomerByRef_AndGoesFull()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        var repo = new FakeRepo(customer);
        var sut = new ApplySubscriptionChange(repo);
        var renews = DateTimeOffset.UtcNow.AddDays(30);

        // Act — first event has no stored StripeCustomerId link yet; mapped via CustomerRef.
        await sut.HandleAsync(new SubscriptionChange("cus_1", "sub_1", SubscriptionState.Active, renews, customer.Id.Value), default);

        // Assert
        customer.Plan.Should().Be(CustomerPlan.Full);
        customer.StripeCustomerId.Should().Be("cus_1");
        customer.StripeSubscriptionId.Should().Be("sub_1");
    }

    [Fact]
    public async Task Canceled_DowngradesToFree()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        customer.LinkStripeCustomer("cus_1");
        customer.ActivateFull("sub_1", DateTimeOffset.UtcNow.AddDays(30));
        var repo = new FakeRepo(customer);
        var sut = new ApplySubscriptionChange(repo);

        // Act — mapped by stored StripeCustomerId, no CustomerRef needed.
        await sut.HandleAsync(new SubscriptionChange("cus_1", "sub_1", SubscriptionState.Canceled, null, null), default);

        // Assert
        customer.Plan.Should().Be(CustomerPlan.Free);
    }

    [Fact]
    public async Task Active_ReplayedTwice_IsIdempotent()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        var repo = new FakeRepo(customer);
        var sut = new ApplySubscriptionChange(repo);
        var change = new SubscriptionChange("cus_1", "sub_1", SubscriptionState.Active, null, customer.Id.Value);

        // Act
        await sut.HandleAsync(change, default);
        await sut.HandleAsync(change, default);

        // Assert
        customer.Plan.Should().Be(CustomerPlan.Full);
        customer.StripeCustomerId.Should().Be("cus_1");
    }

    [Fact]
    public async Task UnknownCustomer_IsIgnored_NoThrow()
    {
        // Arrange
        var repo = new FakeRepo(null);
        var sut = new ApplySubscriptionChange(repo);

        // Act
        Func<Task> act = () => sut.HandleAsync(new SubscriptionChange("cus_x", "sub_x", SubscriptionState.Active, null, Guid.NewGuid()), default);

        // Assert — a stray event for an unknown customer must not throw (Stripe would retry forever).
        await act.Should().NotThrowAsync();
    }

    // Minimal fake: resolves by StripeCustomerId first, else by id (CustomerRef).
    private sealed class FakeRepo(Customer? seed) : ICustomerRepository
    {
        private readonly Customer? _c = seed;
        public Task<Customer?> GetByStripeCustomerIdAsync(string sid, CancellationToken ct) =>
            Task.FromResult(_c?.StripeCustomerId == sid ? _c : null);
        public Task<Customer?> GetAsync(CustomerId id, CancellationToken ct) =>
            Task.FromResult(_c is not null && _c.Id == id ? _c : null);
        public Task UpdateAsync(Customer c, CancellationToken ct) => Task.CompletedTask;
        // The rest are unused here — throw NotImplementedException.
        public Task<IReadOnlyList<Customer>> AllAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task AddAsync(Customer c, CancellationToken ct) => throw new NotImplementedException();
        public Task<Customer?> GetByNameAsync(string n, CancellationToken ct) => throw new NotImplementedException();
        public Task DeleteAsync(CustomerId id, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> IdsDueForHarvestAsync(int t, CancellationToken ct) => throw new NotImplementedException();
    }
}

using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Billing;

public interface IApplySubscriptionChange
{
    Task HandleAsync(SubscriptionChange change, CancellationToken ct);
}

internal sealed class ApplySubscriptionChange(ICustomerRepository customers) : IApplySubscriptionChange
{
    public async Task HandleAsync(SubscriptionChange change, CancellationToken ct)
    {
        // Resolve by the stored Stripe customer id; on first activation fall back to CustomerRef
        // (Stripe's client_reference_id = our CustomerId).
        var customer = await customers.GetByStripeCustomerIdAsync(change.StripeCustomerId, ct);
        if (customer is null && change.CustomerRef is { } refId)
            customer = await customers.GetAsync(new CustomerId(refId), ct);
        if (customer is null) return;   // stray/unknown → ignore (don't make Stripe retry forever)

        if (string.IsNullOrEmpty(customer.StripeCustomerId))
            customer.LinkStripeCustomer(change.StripeCustomerId);

        if (change.State == SubscriptionState.Active)
            customer.ActivateFull(change.SubscriptionId ?? customer.StripeSubscriptionId ?? "", change.RenewsAt);
        else
            customer.DowngradeToFree();

        await customers.UpdateAsync(customer, ct);
    }
}

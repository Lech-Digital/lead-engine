using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Billing;

public interface IApplySubscriptionChange
{
    Task HandleAsync(SubscriptionChange change, CancellationToken ct);
}

internal sealed class ApplySubscriptionChange(ICustomerRepository customers, ITenantScope tenantScope) : IApplySubscriptionChange
{
    public async Task HandleAsync(SubscriptionChange change, CancellationToken ct)
    {
        // Resolve by the stored Stripe customer id; on first activation fall back to CustomerRef
        // (Stripe's client_reference_id = our CustomerId).
        var customer = await customers.GetByStripeCustomerIdAsync(change.StripeCustomerId, ct);
        if (customer is null && change.CustomerRef is { } refId)
        {
            // A webhook carries no HTTP tenant; scope to the referenced customer so the
            // fail-closed filter admits the read (same pattern as the background harvest).
            tenantScope.SetCurrent(refId);
            customer = await customers.GetAsync(new CustomerId(refId), ct);
        }
        if (customer is null) return;   // stray/unknown → ignore (don't make Stripe retry forever)

        if (string.IsNullOrEmpty(customer.StripeCustomerId))
            customer.LinkStripeCustomer(change.StripeCustomerId);

        if (change.State == SubscriptionState.Active)
            customer.ActivateFull(change.SubscriptionId ?? customer.StripeSubscriptionId ?? "", change.RenewsAt);
        else
            customer.DowngradeToFree(DateTimeOffset.UtcNow);   // starts the 60-day retention clock

        await customers.UpdateAsync(customer, ct);
    }
}

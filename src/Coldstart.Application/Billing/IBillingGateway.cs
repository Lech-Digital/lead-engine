namespace Coldstart.Application.Billing;

public enum SubscriptionState { Active, Canceled }

// Currency: lowercase ISO code the prices carry as currency_options ("eur", "usd", "gbp");
// null lets the price's default currency (EUR) apply.
public sealed record CheckoutRequest(Guid CustomerId, string? StripeCustomerId, string CustomerEmail, string SuccessUrl, string CancelUrl, string? Currency = null);
public sealed record SubscriptionChange(string StripeCustomerId, string? SubscriptionId, SubscriptionState State, DateTimeOffset? RenewsAt, Guid? CustomerRef);

public interface IBillingGateway
{
    Task<string> CreateCheckoutSessionUrlAsync(CheckoutRequest req, CancellationToken ct);
    Task<string> CreatePortalSessionUrlAsync(string stripeCustomerId, string returnUrl, CancellationToken ct);
}

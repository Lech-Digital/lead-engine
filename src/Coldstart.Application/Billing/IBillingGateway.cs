namespace Coldstart.Application.Billing;

public enum SubscriptionState { Active, Canceled }

public sealed record CheckoutRequest(Guid CustomerId, string? StripeCustomerId, string CustomerEmail, string SuccessUrl, string CancelUrl);
public sealed record SubscriptionChange(string StripeCustomerId, string? SubscriptionId, SubscriptionState State, DateTimeOffset? RenewsAt, Guid? CustomerRef);

public interface IBillingGateway
{
    Task<string> CreateCheckoutSessionUrlAsync(CheckoutRequest req, CancellationToken ct);
    Task<string> CreatePortalSessionUrlAsync(string stripeCustomerId, string returnUrl, CancellationToken ct);
}

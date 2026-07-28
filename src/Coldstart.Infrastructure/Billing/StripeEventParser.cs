using Coldstart.Application.Billing;
using Stripe;

namespace Coldstart.Infrastructure.Billing;

internal sealed class StripeEventParser : IStripeEventParser
{
    public SubscriptionChange? Parse(string json, string signatureHeader, string webhookSecret)
    {
        // Throws StripeException on a bad/missing signature — the webhook endpoint maps that to 400.
        // The dashboard may pin a different API version than the SDK; that mismatch is not a security issue.
        var stripeEvent = EventUtility.ConstructEvent(json, signatureHeader, webhookSecret, throwOnApiVersionMismatch: false);

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
            {
                var session = (Stripe.Checkout.Session)stripeEvent.Data.Object;
                if (string.IsNullOrEmpty(session.CustomerId)) return null;
                Guid? customerRef = Guid.TryParse(session.ClientReferenceId, out var refId) ? refId : null;
                // RenewsAt is unknown here; the customer.subscription.updated event that follows carries it.
                return new SubscriptionChange(session.CustomerId, session.SubscriptionId, SubscriptionState.Active, null, customerRef);
            }
            case "customer.subscription.updated":
            case "customer.subscription.deleted":
            {
                var subscription = (Subscription)stripeEvent.Data.Object;
                var state = stripeEvent.Type == "customer.subscription.deleted"
                    ? SubscriptionState.Canceled
                    : subscription.Status is "active" or "trialing" or "past_due"
                        ? SubscriptionState.Active
                        : SubscriptionState.Canceled;
                var periodEnd = subscription.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd;
                DateTimeOffset? renewsAt = periodEnd is { } end && end != default
                    ? new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Utc))
                    : null;
                return new SubscriptionChange(subscription.CustomerId, subscription.Id, state, renewsAt, null);
            }
            default:
                return null;
        }
    }
}

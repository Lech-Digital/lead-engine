namespace Coldstart.Application.Billing;

public interface IStripeEventParser
{
    // Verifies the signature (throws on failure) and maps a handled event to a SubscriptionChange (null if not handled).
    SubscriptionChange? Parse(string json, string signatureHeader, string webhookSecret);
}

using Coldstart.Application.Billing;

namespace Coldstart.Web.Tests;

// Fake IStripeEventParser: returns a preset SubscriptionChange, or throws to simulate a bad signature.
public sealed class FakeStripeEventParser : IStripeEventParser
{
    public SubscriptionChange? Next { get; set; }
    public Exception? Throw { get; set; }

    public SubscriptionChange? Parse(string json, string signatureHeader, string webhookSecret)
    {
        if (Throw is not null) throw Throw;
        return Next;
    }
}

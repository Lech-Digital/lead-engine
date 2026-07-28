using Coldstart.Application.Billing;

namespace Coldstart.Web.Tests;

// Fake IBillingGateway: records the last checkout request; returns settable Stripe URLs.
public sealed class FakeBillingGateway : IBillingGateway
{
    public CheckoutRequest? LastCheckout { get; private set; }
    public string CheckoutUrl { get; set; } = "https://stripe.test/checkout";
    public string PortalUrl { get; set; } = "https://stripe.test/portal";

    public Task<string> CreateCheckoutSessionUrlAsync(CheckoutRequest req, CancellationToken ct)
    {
        LastCheckout = req;
        return Task.FromResult(CheckoutUrl);
    }

    public Task<string> CreatePortalSessionUrlAsync(string stripeCustomerId, string returnUrl, CancellationToken ct) =>
        Task.FromResult(PortalUrl);
}

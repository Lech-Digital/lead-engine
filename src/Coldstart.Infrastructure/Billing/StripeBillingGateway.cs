using Coldstart.Application.Billing;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Coldstart.Infrastructure.Billing;

internal sealed class StripeBillingGateway(IOptions<StripeOptions> options) : IBillingGateway
{
    private readonly StripeOptions _opts = options.Value;
    // Null key (unconfigured) is allowed at construction; the SDK throws at call time instead,
    // which the billing page maps to a friendly notice.
    private readonly IStripeClient _client =
        new StripeClient(string.IsNullOrWhiteSpace(options.Value.SecretKey) ? null : options.Value.SecretKey);

    public async Task<string> CreateCheckoutSessionUrlAsync(CheckoutRequest req, CancellationToken ct)
    {
        var lineItems = new List<SessionLineItemOptions>
        {
            new() { Price = _opts.PriceMonthlyId, Quantity = 1 },
        };
        if (_opts.PriceSetupId.Length > 0)
            lineItems.Add(new SessionLineItemOptions { Price = _opts.PriceSetupId, Quantity = 1 });

        // EUR: pin the method list — Stripe's dynamic selection silently drops iDEAL for
        // subscriptions (iDEAL→Wero transition), while an explicit request works. Google Pay /
        // Apple Pay ride on "card" either way. Non-EUR stays dynamic (iDEAL is euro-only).
        var isEur = req.Currency is null or "eur";
        var createOptions = new SessionCreateOptions
        {
            Mode = "subscription",
            LineItems = lineItems,
            Currency = req.Currency,
            PaymentMethodTypes = isEur ? ["card", "ideal", "sepa_debit"] : null,
            AutomaticTax = new SessionAutomaticTaxOptions { Enabled = true },
            TaxIdCollection = new SessionTaxIdCollectionOptions { Enabled = true },
            ClientReferenceId = req.CustomerId.ToString(),
            SuccessUrl = req.SuccessUrl,
            CancelUrl = req.CancelUrl,
        };

        if (!string.IsNullOrEmpty(req.StripeCustomerId))
        {
            createOptions.Customer = req.StripeCustomerId;
            // Stripe Tax + tax-id collection on an existing customer require permission to update these.
            createOptions.CustomerUpdate = new SessionCustomerUpdateOptions { Address = "auto", Name = "auto" };
        }
        else
        {
            createOptions.CustomerEmail = req.CustomerEmail;
        }

        var session = await new SessionService(_client).CreateAsync(createOptions, cancellationToken: ct);
        return session.Url;
    }

    public async Task<string> CreatePortalSessionUrlAsync(string stripeCustomerId, string returnUrl, CancellationToken ct)
    {
        var session = await new Stripe.BillingPortal.SessionService(_client).CreateAsync(
            new Stripe.BillingPortal.SessionCreateOptions { Customer = stripeCustomerId, ReturnUrl = returnUrl },
            cancellationToken: ct);
        return session.Url;
    }
}

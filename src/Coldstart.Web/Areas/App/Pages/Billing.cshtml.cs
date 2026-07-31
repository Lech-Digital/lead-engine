using Coldstart.Application.Billing;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages;

[Authorize]
public sealed class BillingModel(UserManager<ApplicationUser> users, ICustomerRepository customers, IBillingGateway gateway) : PageModel
{
    [TempData] public string? StatusMessage { get; set; }

    public CustomerPlan Plan { get; private set; }
    public DateTimeOffset? RenewsAt { get; private set; }
    public bool HasStripeCustomer { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var customer = await ResolveOwnCustomerAsync(ct);
        if (customer is null) return RedirectToPage("Index");
        Plan = customer.Plan;
        RenewsAt = customer.PlanRenewsAt;
        HasStripeCustomer = !string.IsNullOrEmpty(customer.StripeCustomerId);
        return Page();
    }

    // The prices carry currency_options for exactly these; anything else falls back to EUR.
    private static readonly string[] SupportedCurrencies = ["eur", "usd", "gbp"];

    public async Task<IActionResult> OnPostCheckoutAsync(string? currency, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        var customer = await ResolveOwnCustomerAsync(ct);
        if (user is null || customer is null) return RedirectToPage("Index");
        var chosen = SupportedCurrencies.Contains(currency?.ToLowerInvariant()) ? currency!.ToLowerInvariant() : null;
        try
        {
            var origin = $"{Request.Scheme}://{Request.Host}";
            var url = await gateway.CreateCheckoutSessionUrlAsync(
                new CheckoutRequest(customer.Id.Value, customer.StripeCustomerId,
                    user.Email ?? "", $"{origin}/app?upgraded=1", $"{origin}/app", chosen), ct);
            return Redirect(url);
        }
        catch
        {
            StatusMessage = "Billing isn't available right now — try again in a minute, or email andrzej@getcoldstart.nl.";
            return RedirectToPage("Index");
        }
    }

    public async Task<IActionResult> OnPostPortalAsync(CancellationToken ct)
    {
        var customer = await ResolveOwnCustomerAsync(ct);
        if (customer is null) return RedirectToPage("Index");
        if (string.IsNullOrEmpty(customer.StripeCustomerId))
        {
            StatusMessage = "There's no subscription to manage yet.";
            return RedirectToPage("Index");
        }
        try
        {
            var origin = $"{Request.Scheme}://{Request.Host}";
            var url = await gateway.CreatePortalSessionUrlAsync(customer.StripeCustomerId, $"{origin}/app", ct);
            return Redirect(url);
        }
        catch
        {
            StatusMessage = "Billing isn't available right now — try again in a minute, or email andrzej@getcoldstart.nl.";
            return RedirectToPage("Index");
        }
    }

    // Always the signed-in user's own customer — never an id from the request.
    private async Task<Customer?> ResolveOwnCustomerAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return null;
        return await customers.GetAsync(new CustomerId(user.CustomerId.Value), ct);
    }
}

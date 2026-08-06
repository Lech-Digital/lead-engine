using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages.Settings;

// Read-only subscription status; the checkout/portal actions post to the existing
// /Billing handlers (single owner of the Stripe flows).
[Authorize]
public sealed class BillingSettingsModel(UserManager<ApplicationUser> users, ICustomerRepository customers) : PageModel
{
    [TempData] public string? StatusMessage { get; set; }
    public CustomerPlan Plan { get; private set; }
    public DateTimeOffset? RenewsAt { get; private set; }
    public bool HasStripeCustomer { get; private set; }
    public DateTimeOffset? ComplimentaryUntil { get; private set; }

    // A complimentary grant sets Plan = Full without any Stripe subscription behind it. Those
    // customers have no card on file and nothing to cancel, so none of the paid-subscription
    // copy (or the portal button) is true for them.
    public bool IsComplimentary => Plan == CustomerPlan.Full
        && ComplimentaryUntil is not null
        && !HasStripeSubscription;

    private bool HasStripeSubscription { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });
        var customer = await customers.GetAsync(new CustomerId(user.CustomerId.Value), ct);
        if (customer is null) return RedirectToPage("/Index", new { area = "App" });
        Plan = customer.Plan;
        RenewsAt = customer.PlanRenewsAt;
        HasStripeCustomer = !string.IsNullOrEmpty(customer.StripeCustomerId);
        HasStripeSubscription = !string.IsNullOrEmpty(customer.StripeSubscriptionId);
        ComplimentaryUntil = customer.ComplimentaryUntil;
        return Page();
    }
}

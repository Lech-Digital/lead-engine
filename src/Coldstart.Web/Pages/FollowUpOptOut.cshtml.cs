using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Web.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Pages;

// One-click unsubscribe from trial follow-up emails. Anonymous by design: the token is a
// DataProtection-encrypted customer id minted by EmailTrialFollowUpSender — unforgeable,
// and useless for anything beyond switching these reminders off.
[AllowAnonymous]
public class FollowUpOptOutModel(
    IDataProtectionProvider dataProtection,
    ITenantScope tenants,
    ICustomerRepository customers) : PageModel
{
    public bool Success { get; private set; }

    public async Task OnGetAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return;

        Guid customerId;
        try
        {
            var unprotected = dataProtection.CreateProtector(EmailTrialFollowUpSender.OptOutPurpose).Unprotect(token);
            if (!Guid.TryParse(unprotected, out customerId)) return;
        }
        catch (Exception)
        {
            return;   // tampered or truncated token — render the fallback message
        }

        tenants.SetCurrent(customerId);
        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is null) return;

        customer.OptOutOfFollowUps();
        await customers.UpdateAsync(customer, ct);
        Success = true;
    }
}

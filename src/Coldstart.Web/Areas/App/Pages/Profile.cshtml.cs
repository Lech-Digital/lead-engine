using Coldstart.Application.Customers;
using Coldstart.Application.Hints;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages;

[Authorize]
public sealed class ProfileModel(UserManager<ApplicationUser> users, IGetCompanyProfile getProfile, IUpdateCompanyProfile updateProfile) : PageModel
{
    [TempData] public string? StatusMessage { get; set; }

    [BindProperty] public ProfileInput Input { get; set; } = new();

    public sealed class ProfileInput
    {
        public string? CompanyName { get; set; }
        public string? Address { get; set; }
        public string? VatNumber { get; set; }
        public string? KvkNumber { get; set; }
        public string? Niche { get; set; }
        public string? Website { get; set; }
        public string? ContactEmail { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("Index");
        var v = await getProfile.ForCustomerAsync(user.CustomerId.Value, ct);
        Input = new ProfileInput { CompanyName = v.CompanyName, Address = v.Address, VatNumber = v.VatNumber, KvkNumber = v.KvkNumber, Niche = v.Niche, Website = v.Website, ContactEmail = v.ContactEmail };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("Index");
        await updateProfile.UpdateAsync(user.CustomerId.Value,
            new CompanyProfileInput(Input.CompanyName, Input.Address, Input.VatNumber, Input.KvkNumber, Input.Niche, Input.Website, Input.ContactEmail), ct);
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostShowHintsAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is not null)
        {
            var reset = HintPicker.Reset();
            user.HintsSeenCsv = reset.SeenKeys.Count == 0 ? null : string.Join(',', reset.SeenKeys);
            user.HintShownOn = reset.ShownOn;
            user.HintDismissedOn = reset.DismissedOn;
            await users.UpdateAsync(user);
        }
        StatusMessage = "Onboarding hints will show again.";
        return RedirectToPage();
    }
}

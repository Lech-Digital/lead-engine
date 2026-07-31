using Coldstart.Application.Customers;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages.Settings;

[Authorize]
public sealed class CompanySettingsModel(UserManager<ApplicationUser> users, IGetCompanyProfile getProfile, IUpdateCompanyProfile updateProfile) : PageModel
{
    [TempData] public string? StatusMessage { get; set; }
    [BindProperty] public CompanyInput Input { get; set; } = new();

    public sealed class CompanyInput
    {
        public string? CompanyName { get; set; }
        public string? Website { get; set; }
        public string? Niche { get; set; }
        public string? ContactEmail { get; set; }
        public string? Address { get; set; }
        public string? VatNumber { get; set; }
        public string? KvkNumber { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });
        var v = await getProfile.ForCustomerAsync(user.CustomerId.Value, ct);
        Input = new CompanyInput
        {
            CompanyName = v.CompanyName, Website = v.Website, Niche = v.Niche, ContactEmail = v.ContactEmail,
            Address = v.Address, VatNumber = v.VatNumber, KvkNumber = v.KvkNumber,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });
        // This page owns the company-facing fields only; everything else passes through.
        var v = await getProfile.ForCustomerAsync(user.CustomerId.Value, ct);
        await updateProfile.UpdateAsync(user.CustomerId.Value, new CompanyProfileInput(
            Input.CompanyName, Input.Address, Input.VatNumber, Input.KvkNumber, Input.Niche,
            Input.Website, Input.ContactEmail,
            v.Brief, v.MentionSourceTool, v.PitchLine, v.CtaLine, v.CtaButtonText, v.CtaButtonUrl,
            v.IntroLine, v.SubjectTemplate), ct);
        StatusMessage = "Saved.";
        return RedirectToPage();
    }
}

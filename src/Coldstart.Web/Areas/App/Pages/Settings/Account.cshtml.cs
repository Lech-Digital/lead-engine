using Coldstart.Application.Hints;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages.Settings;

[Authorize]
public sealed class AccountSettingsModel(UserManager<ApplicationUser> users) : PageModel
{
    [TempData] public string? StatusMessage { get; set; }
    public string? SignedInEmail { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Index", new { area = "App" });
        SignedInEmail = user.Email;
        return Page();
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

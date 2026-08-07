using Coldstart.Application.Customers;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages.Settings;

[Authorize]
public sealed class TargetingSettingsModel(
    UserManager<ApplicationUser> users,
    IGetTargeting getTargeting,
    IUpdateTargeting updateTargeting,
    IGetCompanyProfile getProfile,
    ITargetingGenerator generator) : PageModel
{
    [TempData] public string? StatusMessage { get; set; }
    [BindProperty] public TargetingFormInput Input { get; set; } = new();

    public sealed class TargetingFormInput
    {
        public string? Industries { get; set; }
        public string? Geographies { get; set; }
        public string? BuyerRoles { get; set; }
        public int? SizeMin { get; set; }
        public int? SizeMax { get; set; }
        public string? TriggerEvents { get; set; }
        public string? Exclusions { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });
        var v = await getTargeting.ForCustomerAsync(user.CustomerId.Value, ct);
        if (v is not null) Input = FromView(v);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });

        var result = await updateTargeting.UpdateAsync(user.CustomerId.Value, new TargetingInput(
            Input.Industries, Input.Geographies, Input.BuyerRoles,
            Input.SizeMin, Input.SizeMax, Input.TriggerEvents, Input.Exclusions), ct);
        if (!result.Succeeded)
        {
            // Nothing was persisted (UpdateTargeting validates before it touches the repository) —
            // re-render exactly what the customer typed, with the reason it wasn't saved.
            ModelState.AddModelError(string.Empty, result.Message ?? "That couldn't be saved.");
            return Page();
        }
        StatusMessage = "Saved.";
        return RedirectToPage();
    }

    // Best-effort: fills the form from the customer's own description but never saves — the
    // human reviews the proposal and presses Save themselves. A proposal is a suggestion, not
    // a write. On failure the stored values are reloaded and shown untouched: a customer who
    // loses their targeting because an LLM call failed would be a serious bug, not a shrug.
    public async Task<IActionResult> OnPostSuggestAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });

        var profile = await getProfile.ForCustomerAsync(user.CustomerId.Value, ct);
        var proposal = await generator.ProposeAsync(new TargetingProposalRequest(
            profile.CompanyName ?? "", profile.Niche ?? "", profile.Brief ?? "", profile.PitchLine ?? ""), ct);

        ModelState.Clear();
        if (proposal is null)
        {
            var stored = await getTargeting.ForCustomerAsync(user.CustomerId.Value, ct);
            if (stored is not null) Input = FromView(stored);
            StatusMessage = "Couldn't suggest a targeting profile just now — your saved values are unchanged.";
            return Page();
        }

        Input = new TargetingFormInput
        {
            Industries = proposal.Industries, Geographies = proposal.Geographies, BuyerRoles = proposal.BuyerRoles,
            SizeMin = proposal.SizeMin, SizeMax = proposal.SizeMax,
            TriggerEvents = proposal.TriggerEvents, Exclusions = proposal.Exclusions,
        };
        StatusMessage = "Suggested from your description — review it below, then click Save to keep it.";
        return Page();
    }

    private static TargetingFormInput FromView(TargetingView v) => new()
    {
        Industries = v.Industries, Geographies = v.Geographies, BuyerRoles = v.BuyerRoles,
        SizeMin = v.SizeMin, SizeMax = v.SizeMax, TriggerEvents = v.TriggerEvents, Exclusions = v.Exclusions,
    };
}

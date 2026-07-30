using System.ComponentModel.DataAnnotations;
using Coldstart.Application.Customers;
using Coldstart.Application.Hints;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages;

[Authorize]
public sealed class ProfileModel(UserManager<ApplicationUser> users, IGetCompanyProfile getProfile, IUpdateCompanyProfile updateProfile, ICompanyBriefPolisher polisher) : PageModel
{
    [TempData] public string? StatusMessage { get; set; }

    [BindProperty] public ProfileInput Input { get; set; } = new();

    // True only immediately after a Polish action, so the polished (unsaved) brief scrolls into view.
    public bool JustPolished { get; private set; }

    public sealed class ProfileInput
    {
        public string? CompanyName { get; set; }
        public string? Address { get; set; }
        public string? VatNumber { get; set; }
        public string? KvkNumber { get; set; }
        public string? Niche { get; set; }
        public string? Website { get; set; }
        public string? ContactEmail { get; set; }
        [StringLength(2000)] public string? Brief { get; set; }
        public bool MentionSourceTool { get; set; }
        public string? PitchLine { get; set; }
        public string? CtaLine { get; set; }
        public string? CtaButtonText { get; set; }
        public string? CtaButtonUrl { get; set; }
        public string? IntroLine { get; set; }
        public string? SubjectTemplate { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("Index");
        var v = await getProfile.ForCustomerAsync(user.CustomerId.Value, ct);
        Input = new ProfileInput
        {
            CompanyName = v.CompanyName, Address = v.Address, VatNumber = v.VatNumber, KvkNumber = v.KvkNumber,
            Niche = v.Niche, Website = v.Website, ContactEmail = v.ContactEmail,
            Brief = v.Brief, MentionSourceTool = v.MentionSourceTool, PitchLine = v.PitchLine, CtaLine = v.CtaLine, CtaButtonText = v.CtaButtonText, CtaButtonUrl = v.CtaButtonUrl, IntroLine = v.IntroLine, SubjectTemplate = v.SubjectTemplate,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("Index");
        // Operator-only fields (dogfood mention, CTA paragraph, button, subject template):
        // non-operators keep their stored values regardless of what the form posts.
        var isOperator = User.IsInRole(ColdstartRoles.SuperAdmin);
        var stored = isOperator ? null : await getProfile.ForCustomerAsync(user.CustomerId.Value, ct);
        await updateProfile.UpdateAsync(user.CustomerId.Value,
            new CompanyProfileInput(Input.CompanyName, Input.Address, Input.VatNumber, Input.KvkNumber, Input.Niche, Input.Website, Input.ContactEmail, Input.Brief,
                isOperator ? Input.MentionSourceTool : stored!.MentionSourceTool,
                Input.PitchLine,
                isOperator ? Input.CtaLine : stored!.CtaLine,
                isOperator ? Input.CtaButtonText : stored!.CtaButtonText,
                isOperator ? Input.CtaButtonUrl : stored!.CtaButtonUrl,
                Input.IntroLine,
                isOperator ? Input.SubjectTemplate : stored!.SubjectTemplate), ct);
        StatusMessage = "Company profile saved.";
        // PRG back to this page (not the briefing) with a fragment, so the confirmation shows here
        // and the browser lands on the form instead of jumping to the very top.
        return RedirectToPage(null, null, null, "outreach");
    }

    public async Task<IActionResult> OnPostPolishAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("Index");

        if (Input.Brief is { Length: > 2000 }) Input.Brief = Input.Brief[..2000]; // defensive cap before it goes to the LLM

        var polished = string.IsNullOrWhiteSpace(Input.Brief) ? null : await polisher.PolishAsync(Input.Brief!, ct);
        if (polished is not null)
        {
            Input.Brief = polished;
            JustPolished = true;
            StatusMessage = "Polished — review it below, then click Save to keep the change.";
        }
        else StatusMessage = "Couldn't polish just now — your text is unchanged.";
        ModelState.Clear(); // otherwise asp-for re-renders the raw posted value instead of the polished one
        return Page(); // re-render with the polished (unsaved) text; admin reviews then Saves
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
        return RedirectToPage(null, null, null, "hints");
    }
}

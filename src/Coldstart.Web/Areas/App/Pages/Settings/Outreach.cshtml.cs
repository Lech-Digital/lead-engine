using Coldstart.Application.Customers;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages.Settings;

[Authorize]
public sealed class OutreachSettingsModel(UserManager<ApplicationUser> users, IGetCompanyProfile getProfile, IUpdateCompanyProfile updateProfile, ICompanyBriefPolisher polisher) : PageModel
{
    [TempData] public string? StatusMessage { get; set; }
    [BindProperty] public OutreachInput Input { get; set; } = new();
    public bool JustPolished { get; private set; }

    public sealed class OutreachInput
    {
        public string? Brief { get; set; }
        public string? PitchLine { get; set; }
        public string? IntroLine { get; set; }
        public string? CtaLine { get; set; }
        public string? CtaButtonText { get; set; }
        public string? CtaButtonUrl { get; set; }
        public string? SubjectTemplate { get; set; }
        public bool MentionSourceTool { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });
        var v = await getProfile.ForCustomerAsync(user.CustomerId.Value, ct);
        Input = new OutreachInput
        {
            Brief = v.Brief, PitchLine = v.PitchLine, IntroLine = v.IntroLine,
            CtaLine = v.CtaLine, CtaButtonText = v.CtaButtonText, CtaButtonUrl = v.CtaButtonUrl,
            SubjectTemplate = v.SubjectTemplate, MentionSourceTool = v.MentionSourceTool,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });
        var v = await getProfile.ForCustomerAsync(user.CustomerId.Value, ct);
        // Operator-only fields: non-operators keep stored values regardless of the post.
        var op = User.IsInRole(ColdstartRoles.SuperAdmin);
        await updateProfile.UpdateAsync(user.CustomerId.Value, new CompanyProfileInput(
            v.CompanyName, v.Address, v.VatNumber, v.KvkNumber, v.Niche, v.Website, v.ContactEmail,
            Input.Brief,
            op ? Input.MentionSourceTool : v.MentionSourceTool,
            Input.PitchLine,
            op ? Input.CtaLine : v.CtaLine,
            op ? Input.CtaButtonText : v.CtaButtonText,
            op ? Input.CtaButtonUrl : v.CtaButtonUrl,
            Input.IntroLine,
            op ? Input.SubjectTemplate : v.SubjectTemplate), ct);
        StatusMessage = "Saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPolishAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });
        if (Input.Brief is { Length: > 2000 }) Input.Brief = Input.Brief[..2000];
        var polished = string.IsNullOrWhiteSpace(Input.Brief) ? null : await polisher.PolishAsync(Input.Brief!, ct);
        if (polished is not null)
        {
            Input.Brief = polished;
            JustPolished = true;
            StatusMessage = "Polished — review it below, then click Save to keep the change.";
        }
        else StatusMessage = "Couldn't polish just now — your text is unchanged.";
        ModelState.Clear();
        return Page();
    }
}

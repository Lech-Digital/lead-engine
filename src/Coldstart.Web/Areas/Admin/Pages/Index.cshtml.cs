using System.ComponentModel.DataAnnotations;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure.Identity;
using Coldstart.Web.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.Admin.Pages;

[Authorize(Roles = ColdstartRoles.SuperAdmin)]
internal sealed class IndexModel(
    AdminUserService admin,
    UserManager<ApplicationUser> users,
    IInquiryRepository inquiries) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? StatusMessage { get; private set; }
    public IReadOnlyList<AdminUserRow> Rows { get; private set; } = [];
    public IReadOnlyList<InquiryRow> Inquiries { get; private set; } = [];
    public (string Email, string Url)? CopyLink { get; private set; }

    public sealed record InquiryRow(Guid Id, string Name, string Email, string Niche, string WhatYouSell, string? Message, DateTimeOffset CreatedAt);

    public async Task OnGetAsync(Guid? useInquiry, CancellationToken ct)
    {
        if (useInquiry is { } inqId)
        {
            var inq = await inquiries.GetAsync(new InquiryId(inqId), ct);
            if (inq is not null)
                Input = new InputModel
                {
                    Email = inq.Email.Value,
                    Niche = inq.Niche,
                    Info = string.IsNullOrWhiteSpace(inq.Message) ? inq.WhatYouSell : $"{inq.WhatYouSell}\n\n{inq.Message}",
                };
        }
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostInviteAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }
        var result = await admin.InviteAsync(new InviteRequest(Input.Email, Input.Company, Input.Niche, Input.Info), ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("Input.Email", result.Message);
            await LoadAsync(ct);
            return Page();
        }
        StatusMessage = result.Message;
        Input = new InputModel();
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostResendAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) { StatusMessage = "No account specified."; return await RenderAsync(ct); }
        StatusMessage = (await admin.ResendAsync(email, ct)).Message;
        return await RenderAsync(ct);
    }

    public async Task<IActionResult> OnPostRevokeAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) { StatusMessage = "No account specified."; return await RenderAsync(ct); }
        StatusMessage = (await admin.RevokeAsync(email, ct)).Message;
        return await RenderAsync(ct);
    }

    public async Task<IActionResult> OnPostCopyLinkAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) { StatusMessage = "No account specified."; return await RenderAsync(ct); }
        var result = await admin.CopyLinkAsync(email, ct);
        if (result.Succeeded) CopyLink = (email, result.SetPasswordLink!);
        return await RenderAsync(ct);
    }

    public async Task<IActionResult> OnPostBlockAsync(string id, CancellationToken ct)
    {
        StatusMessage = await ActByIdAsync(id, email => admin.BlockAsync(email, User.Identity?.Name, ct));
        return await RenderAsync(ct);
    }

    public async Task<IActionResult> OnPostUnblockAsync(string id, CancellationToken ct)
    {
        StatusMessage = await ActByIdAsync(id, email => admin.UnblockAsync(email, User.Identity?.Name, ct));
        return await RenderAsync(ct);
    }

    public async Task<IActionResult> OnPostRemoveAsync(string id, CancellationToken ct)
    {
        StatusMessage = await ActByIdAsync(id, email => admin.RemoveAsync(email, User.Identity?.Name, ct));
        return await RenderAsync(ct);
    }

    // The row buttons post the Identity user id; the service is keyed by email.
    private async Task<string> ActByIdAsync(string id, Func<string, Task<AdminActionResult>> action)
    {
        var user = await users.FindByIdAsync(id);
        if (user?.Email is not { } email) return "No such account.";
        return (await action(email)).Message;
    }

    // Row-action handlers (Resend/Revoke/CopyLink/Block/Unblock/Remove) only post a bare
    // "email"/"id" hidden field. Razor Pages' empty-prefix model-binding fallback then partially
    // binds that value onto the page's [BindProperty] Input model (e.g. "email" -> Input.Email),
    // leaving Input.Company's [Required] validation unsatisfied. Left uncleared, ModelState still
    // carries that spurious error into the re-rendered page, and the Invite form's
    // asp-validation-summary="All" displays it — making a successful row action look like a
    // failure. Clear ModelState and the Input model before rendering so only this handler's own
    // outcome (StatusMessage) is shown.
    private async Task<IActionResult> RenderAsync(CancellationToken ct)
    {
        ModelState.Clear();
        Input = new InputModel();
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Rows = await admin.ListAsync(User.Identity?.Name, ct);
        Inquiries = (await inquiries.RecentAsync(20, ct))
            .Select(i => new InquiryRow(i.Id.Value, i.Name, i.Email.Value, i.Niche, i.WhatYouSell, i.Message, i.CreatedAt))
            .ToList();
    }

    public sealed class InputModel
    {
        [Required] public string Company { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required] public string Niche { get; set; } = "";
        public string? Info { get; set; }
    }
}

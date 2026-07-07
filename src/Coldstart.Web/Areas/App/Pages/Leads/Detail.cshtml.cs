using Coldstart.Application.Leads;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages.Leads;

[Authorize]
public sealed class DetailModel(UserManager<ApplicationUser> users, IGetLeadDetail getDetail, ILeadWorkflow workflow) : PageModel
{
    public LeadDetail Lead { get; private set; } = null!;
    [BindProperty] public LeadInput Input { get; set; } = new();
    [TempData] public string? Notice { get; set; }

    public sealed class LeadInput
    {
        public string Subject { get; set; } = "";
        public string Draft { get; set; } = "";
        public string? Recipient { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var detail = await LoadAsync(id, ct);
        if (detail is null) return RedirectToPage("/Index", new { area = "App" });
        Input = new LeadInput { Subject = detail.Subject, Draft = detail.Draft, Recipient = detail.RecipientEmail };
        return Page();
    }

    public Task<IActionResult> OnPostSaveAsync(Guid id, CancellationToken ct) =>
        RunAsync(id, ct, cid => workflow.SaveDraftAsync(cid, id, Input.Subject, Input.Draft, Input.Recipient, ct));

    public Task<IActionResult> OnPostAcceptAsync(Guid id, CancellationToken ct) =>
        RunAsync(id, ct, cid => workflow.AcceptAsync(cid, id, Input.Subject, Input.Draft, Input.Recipient, ct));

    public Task<IActionResult> OnPostDiscardAsync(Guid id, CancellationToken ct) =>
        RunAsync(id, ct, cid => workflow.DiscardAsync(cid, id, ct));

    public Task<IActionResult> OnPostReopenAsync(Guid id, CancellationToken ct) =>
        RunAsync(id, ct, cid => workflow.ReopenAsync(cid, id, ct));

    public Task<IActionResult> OnPostMarkSentAsync(Guid id, CancellationToken ct) =>
        RunAsync(id, ct, cid => workflow.MarkSentAsync(cid, id, ct));

    private async Task<LeadDetail?> LoadAsync(Guid id, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return null;
        var detail = await getDetail.ForLeadAsync(user.CustomerId.Value, id, ct);
        if (detail is not null) Lead = detail;
        return detail;
    }

    private async Task<IActionResult> RunAsync(Guid id, CancellationToken ct, Func<Guid, Task<LeadActionResult>> action)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });

        var result = await action(user.CustomerId.Value);
        if (result.Status == LeadActionStatus.NotFound)
            return RedirectToPage("/Index", new { area = "App" });
        if (result.Status == LeadActionStatus.Invalid)
        {
            // Re-render the page with the message and the loaded lead.
            if (await LoadAsync(id, ct) is null) return RedirectToPage("/Index", new { area = "App" });
            ModelState.AddModelError(string.Empty, result.Message ?? "That action isn't allowed.");
            return Page();
        }
        return RedirectToPage("/Index", new { area = "App" });
    }
}

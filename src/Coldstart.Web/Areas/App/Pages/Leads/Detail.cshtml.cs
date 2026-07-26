using Coldstart.Application.Leads;
using Coldstart.Infrastructure.Identity;
using Coldstart.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages.Leads;

[Authorize]
public sealed class DetailModel(UserManager<ApplicationUser> users, IGetLeadDetail getDetail, IGetLeadInbox inbox, ILeadWorkflow workflow) : PageModel
{
    public LeadDetail Lead { get; private set; } = null!;
    [BindProperty] public LeadInput Input { get; set; } = new();
    [TempData] public string? Notice { get; set; }

    // Run mode (?run=1): the customer is stepping through the briefing queue lead-by-lead. Read
    // from the raw query/form value rather than binding a bool — "1" doesn't round-trip through
    // the default bool converter the way "true"/"false" do.
    public bool Run { get; private set; }
    public int RunPosition { get; private set; }
    public int RunTotal { get; private set; }

    public sealed class LeadInput
    {
        public string Subject { get; set; } = "";
        public string Draft { get; set; } = "";
        public string? Recipient { get; set; }
        public string? NoteText { get; set; }
        public Guid NoteId { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid id, string? run, CancellationToken ct)
    {
        Run = run == "1";
        var detail = await LoadAsync(id, ct);
        if (detail is null) return RedirectToPage("/Index", new { area = "App" });
        Input = new LeadInput { Subject = detail.Subject, Draft = detail.Draft, Recipient = detail.RecipientEmail };
        if (Run) await LoadRunPositionAsync(id, ct);
        return Page();
    }

    public Task<IActionResult> OnPostSaveAsync(Guid id, CancellationToken ct) =>
        RunAsync(id, ct, cid => workflow.SaveDraftAsync(cid, id, Input.Subject, Input.Draft, Input.Recipient, ct));

    public Task<IActionResult> OnPostDiscardAsync(Guid id, string? run, CancellationToken ct)
    {
        Run = run == "1";
        return RunAsync(id, ct, cid => workflow.DiscardAsync(cid, id, ct), NextOrAppAsync);
    }

    public Task<IActionResult> OnPostReopenAsync(Guid id, CancellationToken ct) =>
        RunAsync(id, ct, cid => workflow.ReopenAsync(cid, id, ct));

    // Send/mark actions post the email form's current fields and persist them FIRST, so they
    // always act on what the operator sees — not a stale stored recipient/draft they edited
    // without clicking "Save draft" (which matches the client-side Gmail/mailto handoff). There is
    // no separate "Approve" step any more: AcceptAsync (save + approve) runs immediately before the
    // send, so a PendingReview lead can be sent directly from its own-inbox panel.
    public Task<IActionResult> OnPostMarkSentAsync(Guid id, string? run, CancellationToken ct)
    {
        Run = run == "1";
        return RunAsync(id, ct, cid => AcceptThenAsync(cid, id, () => workflow.MarkSentAsync(cid, id, ct), ct), NextOrAppAsync);
    }

    public async Task<IActionResult> OnPostSendFromColdstartAsync(Guid id, string? run, CancellationToken ct)
    {
        if (!User.IsInRole(ColdstartRoles.SuperAdmin)) return Forbid();
        Run = run == "1";
        return await RunAsync(id, ct, cid => AcceptThenAsync(cid, id, () => workflow.SendFromColdstartAsync(cid, id, ct), ct), NextOrAppAsync);
    }

    private async Task<LeadActionResult> AcceptThenAsync(Guid customerId, Guid id, Func<Task<LeadActionResult>> then, CancellationToken ct)
    {
        var accepted = await workflow.AcceptAsync(customerId, id, Input.Subject, Input.Draft, Input.Recipient, ct);
        return accepted.IsOk ? await then() : accepted;
    }

    public Task<IActionResult> OnPostAddNoteAsync(Guid id, CancellationToken ct) =>
        NoteActionAsync(id, ct, cid => workflow.AddNoteAsync(cid, id, Guid.NewGuid(), Input.NoteText ?? "", ct));

    public Task<IActionResult> OnPostUpdateNoteAsync(Guid id, CancellationToken ct) =>
        NoteActionAsync(id, ct, cid => workflow.UpdateNoteAsync(cid, id, Input.NoteId, Input.NoteText ?? "", ct));

    public Task<IActionResult> OnPostRemoveNoteAsync(Guid id, CancellationToken ct) =>
        NoteActionAsync(id, ct, cid => workflow.RemoveNoteAsync(cid, id, Input.NoteId, ct));

    private async Task<LeadDetail?> LoadAsync(Guid id, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return null;
        var detail = await getDetail.ForLeadAsync(user.CustomerId.Value, id, ct);
        if (detail is not null) Lead = detail;
        return detail;
    }

    // On successful Mark-as-sent/Skip in run mode, hop straight to the next unfinished lead in the
    // queue (the just-acted lead is no longer PendingReview/Approved, so it naturally drops out).
    private async Task<IActionResult> NextOrAppAsync(Guid customerId, CancellationToken ct)
    {
        if (!Run) return RedirectToPage("/Index", new { area = "App" });
        var next = await NextQueueLeadAsync(customerId, ct);
        return next is null
            ? RedirectToPage("/Index", new { area = "App" })
            : RedirectToPage("/Leads/Detail", new { area = "App", id = next, run = 1 });
    }

    private async Task<Guid?> NextQueueLeadAsync(Guid customerId, CancellationToken ct)
    {
        var result = await inbox.ForCustomerAsync(customerId, ct);
        return result.Leads
            .Where(l => l.Status is "PendingReview" or "Approved")
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefault();
    }

    // Position/count within the capped briefing queue, for the run bar's "Reviewing N of M" copy.
    private async Task LoadRunPositionAsync(Guid id, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return;
        var result = await inbox.ForCustomerAsync(user.CustomerId.Value, ct);
        var queue = result.Leads
            .Where(l => l.Status is "PendingReview" or "Approved")
            .OrderByDescending(l => l.CreatedAt)
            .Take(LeadBriefing.QueueCap)
            .ToList();
        RunTotal = queue.Count;
        var index = queue.FindIndex(l => l.Id == id);
        RunPosition = index >= 0 ? index + 1 : 1;
    }

    private async Task<IActionResult> RunAsync(Guid id, CancellationToken ct, Func<Guid, Task<LeadActionResult>> action,
        Func<Guid, CancellationToken, Task<IActionResult>>? onSuccess = null)
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
            RestoreEmailFieldsIfEmpty();
            if (Run) await LoadRunPositionAsync(id, ct);
            ModelState.AddModelError(string.Empty, result.Message ?? "That action isn't allowed.");
            return Page();
        }
        return onSuccess is not null
            ? await onSuccess(user.CustomerId.Value, ct)
            : RedirectToPage("/Index", new { area = "App" });
    }

    private async Task<IActionResult> NoteActionAsync(Guid id, CancellationToken ct, Func<Guid, Task<LeadActionResult>> action)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });
        var result = await action(user.CustomerId.Value);
        if (result.Status == LeadActionStatus.NotFound) return RedirectToPage("/Index", new { area = "App" });
        if (result.Status == LeadActionStatus.Invalid)
        {
            if (await LoadAsync(id, ct) is null) return RedirectToPage("/Index", new { area = "App" });
            RestoreEmailFieldsIfEmpty();
            ModelState.AddModelError(string.Empty, result.Message ?? "That note couldn't be saved.");
            return Page();
        }
        // Anchor to the notes panel so the page doesn't jump back to the top after a note action.
        return RedirectToPage("/Leads/Detail", pageHandler: null, routeValues: new { area = "App", id }, fragment: "notes");
    }

    // The note forms and the split MarkSent form only post their own fields, so on an Invalid
    // re-render Input.Subject/Draft/Recipient would otherwise arrive empty and blank the email
    // panel (and a subsequent Save would persist the blanks). Restore from the loaded Lead only
    // when the posted value is empty, so forms that DO post these fields (Save/Accept) keep the
    // user's edits.
    private void RestoreEmailFieldsIfEmpty()
    {
        if (string.IsNullOrEmpty(Input.Subject)) Input.Subject = Lead.Subject;
        if (string.IsNullOrEmpty(Input.Draft)) Input.Draft = Lead.Draft;
        if (string.IsNullOrWhiteSpace(Input.Recipient)) Input.Recipient = Lead.RecipientEmail;
    }
}

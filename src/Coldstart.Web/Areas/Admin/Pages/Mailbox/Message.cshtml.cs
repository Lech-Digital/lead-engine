using Coldstart.Application.Mailbox;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.Admin.Pages.Mailbox;

[Authorize(Roles = ColdstartRoles.SuperAdmin)]
public sealed class MessageModel(IMailbox mailbox, ILogger<MessageModel> logger) : PageModel
{
    public MailMessageDetail? Mail { get; private set; }
    public bool Unreachable { get; private set; }

    public async Task<IActionResult> OnGetAsync(uint uid, CancellationToken ct)
    {
        try
        {
            Mail = await mailbox.GetAsync(uid, ct);
            if (Mail is null) return RedirectToPage("/Mailbox/Index", new { area = "Admin" });
            if (!Mail.Seen) await mailbox.MarkReadAsync(uid, read: true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Mailbox unreachable while opening message {Uid}.", uid);
            Unreachable = true;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostMarkUnreadAsync(uint uid, CancellationToken ct)
    {
        try
        {
            await mailbox.MarkReadAsync(uid, read: false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Mailbox unreachable while marking message {Uid} unread.", uid);
            return RedirectToInboxUnreachable();
        }
        return RedirectToPage("/Mailbox/Index", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(uint uid, CancellationToken ct)
    {
        try
        {
            await mailbox.DeleteAsync(uid, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Mailbox unreachable while deleting message {Uid}.", uid);
            return RedirectToInboxUnreachable();
        }
        TempData["MailFlash"] = "Message deleted.";
        return RedirectToPage("/Mailbox/Index", new { area = "Admin" });
    }

    public async Task<IActionResult> OnGetAttachmentAsync(uint uid, int index, CancellationToken ct)
    {
        if (index < 0) return NotFound();
        try
        {
            var att = await mailbox.GetAttachmentAsync(uid, index, ct);
            if (att is null) return NotFound();
            return File(att.Content, att.ContentType, att.FileName);   // always Content-Disposition: attachment
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Mailbox unreachable while downloading attachment {Index} for message {Uid}.", index, uid);
            return RedirectToInboxUnreachable();
        }
    }

    private IActionResult RedirectToInboxUnreachable()
    {
        TempData["MailFlash"] = "Mailbox unreachable — the action didn't complete.";
        return RedirectToPage("/Mailbox/Index", new { area = "Admin" });
    }
}

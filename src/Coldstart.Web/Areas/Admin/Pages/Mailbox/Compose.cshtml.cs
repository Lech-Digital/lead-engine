using System.ComponentModel.DataAnnotations;
using Coldstart.Application.Mailbox;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.Admin.Pages.Mailbox;

[Authorize(Roles = ColdstartRoles.SuperAdmin)]
public sealed class ComposeModel(IMailbox mailbox, ILogger<ComposeModel> logger) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public string? SendError { get; private set; }

    public async Task OnGetAsync(uint? replyTo, CancellationToken ct)
    {
        if (replyTo is null) return;
        try
        {
            var original = await mailbox.GetAsync(replyTo.Value, ct);
            if (original is null) return;
            Input = new InputModel
            {
                To = original.FromAddress,
                Subject = original.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
                    ? original.Subject : $"Re: {original.Subject}",
                Body = "\n\nOn " + original.Date.ToString("dd MMM yyyy HH:mm",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    $", {(string.IsNullOrWhiteSpace(original.FromName) ? original.FromAddress : original.FromName)} wrote:\n" +
                    string.Join("\n", original.TextBody.Split('\n').Select(l => "> " + l)),
                InReplyTo = original.MessageId,
                References = string.IsNullOrEmpty(original.References)
                    ? original.MessageId
                    : $"{original.References} {original.MessageId}",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Mailbox unreachable while preparing reply to {Uid}.", replyTo);
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return Page();
        if (Input.To.Any(c => c is '\r' or '\n') || Input.Subject.Any(c => c is '\r' or '\n'))
        {
            ModelState.AddModelError("", "Line breaks are not allowed in address or subject.");
            return Page();
        }
        try
        {
            await mailbox.SendAsync(new OutgoingMail(
                Input.To, Input.Subject, Input.Body, Input.InReplyTo, Input.References), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Sending mail from the admin mailbox failed.");
            SendError = "Sending failed — the mail server didn't accept the message. Your text is preserved; try again.";
            return Page();
        }
        TempData["MailFlash"] = "Sent.";
        return RedirectToPage("/Mailbox/Index", new { area = "Admin" });
    }

    public sealed class InputModel
    {
        [Required, EmailAddress] public string To { get; set; } = "";
        [Required] public string Subject { get; set; } = "";
        [Required] public string Body { get; set; } = "";
        public string? InReplyTo { get; set; }
        public string? References { get; set; }
    }
}

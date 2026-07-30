using Coldstart.Application.Ports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Pages;

// The human contact page: sends straight to the founder's mailbox. Distinct from
// InquiryModel (/Inquiry), which handles the sample-leads funnel.
public sealed class ContactPageModel(IEmailSender email) : PageModel
{
    private const string NotifyTo = "info@getcoldstart.nl";

    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Message { get; set; } = "";
    [BindProperty] public string? Website { get; set; } // honeypot — must stay empty

    [TempData] public bool Sent { get; set; }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        // Honeypot: bots fill hidden fields. Pretend success without sending.
        if (!string.IsNullOrWhiteSpace(Website))
        {
            Sent = true;
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Email) || !Email.Contains('@')
            || string.IsNullOrWhiteSpace(Message))
        {
            ModelState.AddModelError("", "Name, a valid email, and a message are required.");
            return Page();
        }

        var body = $"Contact form message\n\nFrom: {Name} <{Email}>\n\n{Message.Trim()}";
        try
        {
            await email.SendAsync(NotifyTo, $"Contact form: {Name}", body, null, ct);
        }
        catch
        {
            ModelState.AddModelError("", "Couldn't send just now — please email info@getcoldstart.nl directly.");
            return Page();
        }

        Sent = true;
        return RedirectToPage();
    }
}

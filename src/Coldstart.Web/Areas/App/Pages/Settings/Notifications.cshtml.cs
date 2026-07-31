using Coldstart.Application.Harvest;
using Coldstart.Application.Notifications;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages.Settings;

[Authorize]
public sealed class NotificationsSettingsModel(UserManager<ApplicationUser> users, ICustomerRepository customers, IUpdateNotificationSettings updateNotifications, INotifyBriefing notifier) : PageModel
{
    [TempData] public string? StatusMessage { get; set; }
    [BindProperty] public string? SlackWebhookUrl { get; set; }
    [BindProperty] public string? WhatsAppNumber { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });
        var customer = await customers.GetAsync(new CustomerId(user.CustomerId.Value), ct);
        SlackWebhookUrl = customer?.SlackWebhookUrl;
        WhatsAppNumber = customer?.WhatsAppNumber;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });
        // WhatsApp stays operator-only until the paid Twilio account exists.
        var whatsApp = User.IsInRole(ColdstartRoles.SuperAdmin)
            ? WhatsAppNumber
            : (await customers.GetAsync(new CustomerId(user.CustomerId.Value), ct))?.WhatsAppNumber;
        var error = await updateNotifications.UpdateAsync(user.CustomerId.Value, SlackWebhookUrl, whatsApp, ct);
        StatusMessage = error ?? "Saved.";
        return RedirectToPage();
    }

    public Task<IActionResult> OnPostTestSlackAsync(CancellationToken ct) => TestAsync("slack", ct);
    public Task<IActionResult> OnPostTestWhatsAppAsync(CancellationToken ct) => TestAsync("whatsapp", ct);

    private async Task<IActionResult> TestAsync(string channel, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });
        var sent = await notifier.NotifyAsync(user.CustomerId.Value,
            [new SavedLead(Guid.NewGuid(), "Coldstart test", "If you can read this, the channel works.")],
            channelFilter: channel, ct);
        StatusMessage = sent.Count > 0
            ? $"Test sent via {channel} — check your messages."
            : $"Couldn't send the {channel} test — save a valid destination first.";
        return RedirectToPage();
    }
}

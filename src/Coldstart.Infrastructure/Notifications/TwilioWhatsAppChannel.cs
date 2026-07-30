using System.Net.Http.Headers;
using System.Text;
using Coldstart.Application.Notifications;
using Coldstart.Domain;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Notifications;

// Sends the briefing summary over WhatsApp via Twilio's Messages API. Global operator
// credentials; per-customer destination number. Soft-degrade: without Twilio config the
// channel simply reports "can't send".
internal sealed class TwilioWhatsAppChannel(HttpClient http, IOptions<TwilioOptions> twilio, IOptions<NotificationsOptions> options) : IBriefingChannel
{
    public string Name => "whatsapp";

    public bool CanSend(Customer customer) =>
        twilio.Value.IsConfigured && !string.IsNullOrWhiteSpace(customer.WhatsAppNumber);

    public async Task SendAsync(Customer customer, BriefingNotification notification, CancellationToken ct)
    {
        var t = twilio.Value;
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{t.AccountSid}/Messages.json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{t.AccountSid}:{t.AuthToken}")));
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["From"] = $"whatsapp:{t.WhatsAppFrom}",
            ["To"] = $"whatsapp:{customer.WhatsAppNumber}",
            ["Body"] = BuildText(notification, options.Value.BaseUrl),
        });
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    // WhatsApp renders link-heavy messages poorly — company names as plain text, one link.
    internal static string BuildText(BriefingNotification n, string baseUrl)
    {
        var sb = new StringBuilder();
        sb.Append($"☀️ Coldstart: {n.Leads.Count} new {(n.Leads.Count == 1 ? "lead" : "leads")} for {n.CustomerName}\n");
        foreach (var lead in n.Leads.Take(6))
            sb.Append($"• {lead.Company} — {lead.Subject}\n");
        if (n.Leads.Count > 6) sb.Append($"…and {n.Leads.Count - 6} more\n");
        sb.Append($"Review them: {baseUrl.TrimEnd('/')}/app");
        return sb.ToString();
    }
}

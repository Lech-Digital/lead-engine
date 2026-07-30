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
        var fields = new Dictionary<string, string>
        {
            ["From"] = $"whatsapp:{t.WhatsAppFrom}",
            ["To"] = $"whatsapp:{customer.WhatsAppNumber}",
        };
        if (string.IsNullOrWhiteSpace(t.ContentSid))
        {
            fields["Body"] = BuildText(notification, options.Value.BaseUrl);
        }
        else
        {
            // Production: approved template. Variables must be single-line.
            fields["ContentSid"] = t.ContentSid;
            fields["ContentVariables"] = BuildTemplateVariables(notification);
        }
        req.Content = new FormUrlEncodedContent(fields);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    // Template variables for the approved briefing template:
    //   {{1}} = lead count, {{2}} = account name, {{3}} = company list (single line).
    internal static string BuildTemplateVariables(BriefingNotification n)
    {
        var companies = string.Join(", ", n.Leads.Take(5).Select(l => Clean(l.Company)));
        if (n.Leads.Count > 5) companies += $" +{n.Leads.Count - 5} more";
        return System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["1"] = n.Leads.Count.ToString(),
            ["2"] = Clean(n.CustomerName),
            ["3"] = companies,
        });
        static string Clean(string s) => s.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
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

using System.Net.Http.Json;
using System.Text;
using Coldstart.Application.Notifications;
using Coldstart.Domain;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Notifications;

// Posts the briefing summary to the customer's Slack incoming webhook.
internal sealed class SlackBriefingChannel(HttpClient http, IOptions<NotificationsOptions> options) : IBriefingChannel
{
    public string Name => "slack";

    public bool CanSend(Customer customer) => !string.IsNullOrWhiteSpace(customer.SlackWebhookUrl);

    public async Task SendAsync(Customer customer, BriefingNotification notification, CancellationToken ct)
    {
        var resp = await http.PostAsJsonAsync(customer.SlackWebhookUrl!,
            new { text = BuildText(notification, options.Value.BaseUrl) }, ct);
        resp.EnsureSuccessStatusCode();
    }

    internal static string BuildText(BriefingNotification n, string baseUrl)
    {
        var b = baseUrl.TrimEnd('/');
        var sb = new StringBuilder();
        sb.Append($"☀️ Coldstart: {n.Leads.Count} new {(n.Leads.Count == 1 ? "lead" : "leads")} for {n.CustomerName}\n");
        foreach (var lead in n.Leads.Take(6))
            sb.Append($"• <{b}/app/leads/{lead.Id}|{lead.Company}> — {lead.Subject}\n");
        if (n.Leads.Count > 6) sb.Append($"…and {n.Leads.Count - 6} more\n");
        sb.Append($"Review them: {b}/app");
        return sb.ToString();
    }
}

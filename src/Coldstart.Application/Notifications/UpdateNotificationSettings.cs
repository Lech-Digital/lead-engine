using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Notifications;

public interface IUpdateNotificationSettings
{
    // Returns null on success, or a human-readable validation error.
    Task<string?> UpdateAsync(Guid customerId, string? slackWebhookUrl, string? whatsAppNumber, CancellationToken ct);
}

internal sealed class UpdateNotificationSettings(ICustomerRepository customers) : IUpdateNotificationSettings
{
    public async Task<string?> UpdateAsync(Guid customerId, string? slackWebhookUrl, string? whatsAppNumber, CancellationToken ct)
    {
        var slack = string.IsNullOrWhiteSpace(slackWebhookUrl) ? null : slackWebhookUrl.Trim();
        var whatsApp = string.IsNullOrWhiteSpace(whatsAppNumber) ? null : whatsAppNumber.Trim().Replace(" ", "");

        if (slack is not null && !slack.StartsWith("https://hooks.slack.com/", StringComparison.Ordinal))
            return "The Slack URL should be an incoming-webhook URL (it starts with https://hooks.slack.com/).";
        if (whatsApp is not null && !(whatsApp.StartsWith('+') && whatsApp.Length is >= 9 and <= 16 && whatsApp.Skip(1).All(char.IsDigit)))
            return "The WhatsApp number should be in international format, e.g. +31612345678.";

        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is null) return "Account not found.";

        customer.ConfigureNotifications(slack, whatsApp);
        await customers.UpdateAsync(customer, ct);
        return null;
    }
}

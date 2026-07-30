using Coldstart.Domain;

namespace Coldstart.Application.Notifications;

public sealed record BriefingNotification(Guid CustomerId, string CustomerName, IReadOnlyList<Harvest.SavedLead> Leads);

// A push channel (Slack, WhatsApp, …). CanSend answers "is this channel configured for
// this customer"; SendAsync may throw — the dispatcher isolates failures per channel.
public interface IBriefingChannel
{
    string Name { get; }
    bool CanSend(Customer customer);
    Task SendAsync(Customer customer, BriefingNotification notification, CancellationToken ct);
}

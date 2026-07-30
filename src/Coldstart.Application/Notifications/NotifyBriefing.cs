using Coldstart.Application.Harvest;
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Notifications;

public interface INotifyBriefing
{
    // channelFilter: null = all configured channels; a channel Name = just that one
    // (used by the settings page's "send test" buttons). Returns the channel names
    // that were actually sent to.
    Task<IReadOnlyList<string>> NotifyAsync(Guid customerId, IReadOnlyList<SavedLead> leads,
        string? channelFilter = null, CancellationToken ct = default);
}

internal sealed class NotifyBriefing(ICustomerRepository customers, IEnumerable<IBriefingChannel> channels) : INotifyBriefing
{
    public async Task<IReadOnlyList<string>> NotifyAsync(Guid customerId, IReadOnlyList<SavedLead> leads,
        string? channelFilter = null, CancellationToken ct = default)
    {
        if (leads.Count == 0) return [];
        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is null) return [];

        var notification = new BriefingNotification(customerId, customer.Name, leads);
        var sent = new List<string>();
        foreach (var channel in channels)
        {
            if (channelFilter is not null && channel.Name != channelFilter) continue;
            if (!channel.CanSend(customer)) continue;
            try
            {
                await channel.SendAsync(customer, notification, ct);
                sent.Add(channel.Name);
            }
            catch
            {
                // One failing channel must not stop the others — and a notification
                // failure must never fail the harvest that triggered it.
            }
        }
        return sent;
    }
}

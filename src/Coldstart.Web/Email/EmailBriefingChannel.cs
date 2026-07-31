using Coldstart.Application.Notifications;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coldstart.Web.Email;

// The email edition of the morning briefing — always on: this is the product's core
// promise ("wake up to your next client"). Sends to the tenant's account address.
internal sealed class EmailBriefingChannel(ColdstartDbContext db, IEmailSender email, IOptions<NotificationsOptions> options) : IBriefingChannel
{
    public string Name => "email";

    public bool CanSend(Customer customer) => true;

    public async Task SendAsync(Customer customer, BriefingNotification notification, CancellationToken ct)
    {
        var to = await db.Users
            .Where(u => u.CustomerId == customer.Id.Value && u.Email != null && u.EmailConfirmed)
            .OrderBy(u => u.Email)
            .Select(u => u.Email!)
            .FirstOrDefaultAsync(ct);
        if (to is null) return;

        var content = EmailTemplates.MorningBriefing(
            notification.Leads.Count,
            notification.Leads.Select(l => new EmailTemplates.BriefingLead(l.Company, l.Subject)).ToList(),
            $"{options.Value.BaseUrl.TrimEnd('/')}/app");
        await email.SendAsync(to, content.Subject, content.TextBody, content.HtmlBody, ct);
    }
}

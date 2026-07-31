using Coldstart.Application.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Retention;

// Enforces the privacy policy's retention table (§7):
//  - inquiries are kept "at most 12 months" → hard-delete older ones;
//  - customer data is deleted "within 60 days after the subscription ends" → deletion itself
//    stays a human decision (it is destructive and the customer may be re-signing), so at
//    day 53 the operator gets an email naming the deadline.
internal sealed class RetentionSweeper(
    ColdstartDbContext db,
    IEmailSender email,
    IOptions<OpsOptions> ops,
    ILogger<RetentionSweeper> log)
{
    internal const int ReminderAfterDays = 53;

    public async Task<(int InquiriesDeleted, int RemindersSent)> SweepAsync(DateTimeOffset now, CancellationToken ct)
    {
        var inquiriesDeleted = await db.Inquiries
            .Where(i => i.CreatedAt < now.AddMonths(-12))
            .ExecuteDeleteAsync(ct);

        var dueCustomers = await db.Customers.IgnoreQueryFilters()
            .Where(c => c.SubscriptionEndedAt != null
                        && c.SubscriptionEndedAt < now.AddDays(-ReminderAfterDays)
                        && !c.RetentionReminderSent)
            .ToListAsync(ct);

        var reminders = 0;
        foreach (var customer in dueCustomers)
        {
            var deadline = customer.SubscriptionEndedAt!.Value.AddDays(60);
            await email.SendAsync(ops.Value.AlertEmail,
                $"⏰ Retention: delete {customer.Name}'s data by {deadline:d MMMM yyyy}",
                $"The subscription for \"{customer.Name}\" ended on {customer.SubscriptionEndedAt:d MMMM yyyy}.\n\n" +
                $"The privacy policy promises deletion within 60 days — that is {deadline:d MMMM yyyy}.\n\n" +
                "Remove the customer from /admin (or note why retention continues, e.g. a legal hold or a pending re-subscription).",
                null, ct);
            customer.MarkRetentionReminderSent();
            reminders++;
        }
        if (reminders > 0) await db.SaveChangesAsync(ct);

        if (inquiriesDeleted > 0 || reminders > 0)
            log.LogInformation("Retention sweep: {Inquiries} old inquiry(ies) deleted, {Reminders} deletion reminder(s) sent.",
                inquiriesDeleted, reminders);
        return (inquiriesDeleted, reminders);
    }
}

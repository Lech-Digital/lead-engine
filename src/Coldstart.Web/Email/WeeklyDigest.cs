using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coldstart.Web.Email;

public sealed class WeeklyDigestOptions
{
    public bool Enabled { get; set; } = true;
    public DayOfWeek Day { get; set; } = DayOfWeek.Monday;
    public TimeSpan RunAtUtc { get; set; } = new(7, 30, 0);
}

// Monday-morning summaries: one branded email per active customer with something to report,
// and one plain-text heartbeat for the operator (always sent — zeros are information too).
internal sealed class WeeklyDigestSweeper(
    ColdstartDbContext db,
    IEmailSender email,
    IOptions<Coldstart.Infrastructure.OpsOptions> ops,
    IOptions<Coldstart.Infrastructure.Notifications.NotificationsOptions> notifications)
{
    public async Task<(int CustomerDigests, bool OperatorDigest)> SweepAsync(DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-7);
        var appUrl = $"{notifications.Value.BaseUrl.TrimEnd('/')}/app";
        var customers = await db.Customers.IgnoreQueryFilters().ToListAsync(ct);
        var sentDigests = 0;

        foreach (var customer in customers)
        {
            var leads = await db.Leads.IgnoreQueryFilters()
                .Where(l => l.CustomerId == customer.Id)
                .Where(l => l.CreatedAt >= since || l.SentAt >= since || l.OutcomeAt >= since)
                .ToListAsync(ct);
            var found = leads.Count(l => l.CreatedAt >= since && l.Status != LeadStatus.Rejected);
            var sent = leads.Count(l => l.SentAt >= since);
            var replies = leads.Count(l => l.OutcomeAt >= since && l.Outcome != LeadOutcome.None);
            if (found + sent + replies == 0) continue;   // nothing to say — say nothing

            var to = await db.Users
                .Where(u => u.CustomerId == customer.Id.Value && u.Email != null && u.EmailConfirmed)
                .OrderBy(u => u.Email).Select(u => u.Email!).FirstOrDefaultAsync(ct);
            if (to is null) continue;

            var content = EmailTemplates.WeeklyDigest(customer.Name, found, sent, replies, appUrl);
            await email.SendAsync(to, content.Subject, content.TextBody, content.HtmlBody, ct);
            sentDigests++;
        }

        await SendOperatorDigestAsync(since, now, ct);
        return (sentDigests, true);
    }

    private async Task SendOperatorDigestAsync(DateTimeOffset since, DateTimeOffset now, CancellationToken ct)
    {
        var leadsFound = await db.Leads.IgnoreQueryFilters().CountAsync(l => l.CreatedAt >= since && l.Status != LeadStatus.Rejected, ct);
        var leadsSent = await db.Leads.IgnoreQueryFilters().CountAsync(l => l.SentAt >= since, ct);
        var outcomes = await db.Leads.IgnoreQueryFilters().CountAsync(l => l.OutcomeAt >= since && l.Outcome != LeadOutcome.None, ct);
        var inquiries = await db.Inquiries.CountAsync(i => i.CreatedAt >= since, ct);
        var fullPlans = await db.Customers.IgnoreQueryFilters().CountAsync(c => c.Plan == CustomerPlan.Full, ct);
        var runStats = await db.HarvestRunStats.Where(s => s.RanAt >= since)
            .GroupBy(_ => 1).Select(g => new { Read = g.Sum(s => s.Harvested), Runs = g.Count() }).FirstOrDefaultAsync(ct);
        var retentionDue = await db.Customers.IgnoreQueryFilters()
            .Where(c => c.SubscriptionEndedAt != null).Select(c => c.Name).ToListAsync(ct);

        var body =
            $"Coldstart — week to {now:d MMMM yyyy}\n\n" +
            $"  Engine runs:        {runStats?.Runs ?? 0} (read {runStats?.Read ?? 0} signals)\n" +
            $"  Leads found:        {leadsFound}\n" +
            $"  Leads sent:         {leadsSent}\n" +
            $"  Outcomes reported:  {outcomes}\n" +
            $"  New inquiries:      {inquiries}\n" +
            $"  Paying customers:   {fullPlans}\n" +
            (retentionDue.Count > 0 ? $"  Retention clocks:   {string.Join(", ", retentionDue)}\n" : "") +
            "\nDashboards: https://getcoldstart.nl/admin · https://stats.getcoldstart.nl";
        await email.SendAsync(ops.Value.AlertEmail, $"☀️ Coldstart weekly — {leadsFound} found · {leadsSent} sent · {inquiries} inquiries", body, null, ct);
    }
}

internal sealed class WeeklyDigestService(
    IServiceScopeFactory scopes,
    IOptions<WeeklyDigestOptions> options,
    ILogger<WeeklyDigestService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled) { log.LogInformation("Weekly digest disabled."); return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NextWeeklyDelay(opts.Day, opts.RunAtUtc, DateTime.UtcNow);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                using var scope = scopes.CreateScope();
                var (digests, _) = await scope.ServiceProvider.GetRequiredService<WeeklyDigestSweeper>()
                    .SweepAsync(DateTimeOffset.UtcNow, stoppingToken);
                log.LogInformation("Weekly digest: {Count} customer digest(s) + operator digest sent.", digests);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Weekly digest failed.");
            }
        }
    }

    public static TimeSpan NextWeeklyDelay(DayOfWeek day, TimeSpan at, DateTime utcNow)
    {
        var daysAhead = ((int)day - (int)utcNow.DayOfWeek + 7) % 7;
        var next = utcNow.Date.AddDays(daysAhead) + at;
        if (next <= utcNow) next = next.AddDays(7);
        return next - utcNow;
    }
}

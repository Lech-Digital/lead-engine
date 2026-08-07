using Coldstart.Application.Harvest;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coldstart.Infrastructure.Harvest;

// Drains the harvest queue one job at a time (sequential → stays under the Groq rate limit).
// Each job runs in its own DI scope with the tenant set to the job's customer.
internal sealed class HarvestQueueService(
    HarvestQueue queue,
    IServiceScopeFactory scopes,
    Microsoft.Extensions.Options.IOptions<OpsOptions> ops,
    ILogger<HarvestQueueService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try { await RunJobAsync(job, stoppingToken); }
            catch (Exception ex) { log.LogError(ex, "Harvest job for customer {CustomerId} failed", job.CustomerId); }
        }
    }

    private async Task RunJobAsync(HarvestJob job, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ITenantScope>().SetCurrent(job.CustomerId);

        var customers = sp.GetRequiredService<ICustomerRepository>();
        var customer = await customers.GetAsync(new CustomerId(job.CustomerId), ct);
        if (customer is null) return;

        var targeting = HarvestTargeting.FromCustomer(customer.Profile, customer.Targeting, customer.Name);
        if (!targeting.HasKeywords)
        {
            // No usable search profile (generation failed at invite) → mark failed, no bogus leads.
            customer.MarkHarvest(HarvestStatus.Failed, DateTimeOffset.UtcNow);
            await customers.UpdateAsync(customer, ct);
            await AlertOperatorAsync(sp,
                $"Harvest failed for {customer.Name} — no search keywords",
                $"Customer {customer.Name} ({job.CustomerId}) has no usable search profile; the harvest was marked Failed. Fix the profile keywords and re-run.", ct);
            return;
        }

        customer.MarkHarvest(HarvestStatus.Running, null);
        await customers.UpdateAsync(customer, ct);
        try
        {
            var report = await sp.GetRequiredService<IHarvestLeads>().RunForCustomerAsync(job.CustomerId, targeting, ct);
            customer.MarkHarvest(HarvestStatus.Done, DateTimeOffset.UtcNow);
            // Aggregate telemetry for the public engine ticker + operator cockpit.
            var statsDb = sp.GetRequiredService<ColdstartDbContext>();
            statsDb.HarvestRunStats.Add(Domain.HarvestRunStat.Record(DateTimeOffset.UtcNow, report.Harvested, report.Saved));
            await statsDb.SaveChangesAsync(ct);
            if (report.NewLeads.Count > 0)
            {
                // Push the briefing to the customer's configured channels. Best-effort by
                // design — the notifier isolates channel failures and never throws here.
                var sentTo = await sp.GetRequiredService<Application.Notifications.INotifyBriefing>()
                    .NotifyAsync(job.CustomerId, report.NewLeads, channelFilter: null, ct);
                if (sentTo.Count > 0)
                    log.LogInformation("Briefing for {CustomerId} pushed to: {Channels}", job.CustomerId, string.Join(",", sentTo));
            }
            if (HarvestRunAlert.For(report, customer.Name, job.CustomerId) is { } alert)
                await AlertOperatorAsync(sp, alert.Subject, alert.Body, ct);
            log.LogInformation(
                "Harvest for {CustomerId}: harvested {Harvested}, scored-in {ScoredIn}, deduped {Deduped}, enriched {Enriched}, saved {Saved}, rejected {Rejected}",
                job.CustomerId, report.Harvested, report.ScoredIn, report.Deduped, report.Enriched, report.Saved, report.Rejected);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Harvest run for customer {CustomerId} failed", job.CustomerId);
            customer.MarkHarvest(HarvestStatus.Failed, DateTimeOffset.UtcNow);
            await AlertOperatorAsync(sp,
                $"Harvest crashed for {customer.Name}",
                $"The run for {customer.Name} ({job.CustomerId}) threw:\n\n{ex}", ct);
        }
        await customers.UpdateAsync(customer, ct);
    }

    // Ops alert to the operator's mailbox. Best-effort: an alert failure must never
    // affect the harvest itself.
    private async Task AlertOperatorAsync(IServiceProvider sp, string subject, string body, CancellationToken ct)
    {
        try
        {
            await sp.GetRequiredService<Coldstart.Application.Ports.IEmailSender>()
                .SendAsync(ops.Value.AlertEmail, $"⚠️ {subject}", body, null, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Operator alert failed to send");
        }
    }
}

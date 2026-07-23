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

        var targeting = HarvestTargeting.FromProfile(customer.Profile, customer.Name);
        if (!targeting.HasKeywords)
        {
            // No usable search profile (generation failed at invite) → mark failed, no bogus leads.
            customer.MarkHarvest(HarvestStatus.Failed, DateTimeOffset.UtcNow);
            await customers.UpdateAsync(customer, ct);
            return;
        }

        customer.MarkHarvest(HarvestStatus.Running, null);
        await customers.UpdateAsync(customer, ct);
        try
        {
            await sp.GetRequiredService<IHarvestLeads>().RunForCustomerAsync(job.CustomerId, targeting, ct);
            customer.MarkHarvest(HarvestStatus.Done, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Harvest run for customer {CustomerId} failed", job.CustomerId);
            customer.MarkHarvest(HarvestStatus.Failed, DateTimeOffset.UtcNow);
        }
        await customers.UpdateAsync(customer, ct);
    }
}

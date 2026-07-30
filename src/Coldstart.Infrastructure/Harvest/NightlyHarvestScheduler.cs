using Coldstart.Application.Harvest;
using Coldstart.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Harvest;

// Fires once per day at RunAtUtc: enqueues a harvest for every Full-plan customer that has room
// (< TopUpThreshold unfinished leads). The existing HarvestQueueService drains them.
public sealed class NightlyHarvestScheduler(
    IServiceScopeFactory scopes,
    IOptions<HarvestScheduleOptions> options,
    ILogger<NightlyHarvestScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled) { log.LogInformation("Nightly harvest scheduler disabled."); return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NextRunDelay(opts.RunAtUtc, DateTime.UtcNow);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                using var scope = scopes.CreateScope();
                var n = await EnqueueDueAsync(scope.ServiceProvider, opts.TopUpThreshold, stoppingToken);
                log.LogInformation("Nightly harvest: enqueued {Count} Full-plan customer(s).", n);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Nightly harvest scheduling failed.");
            }
        }
    }

    // Delay from utcNow to the next occurrence of runAt (today if still ahead, else tomorrow).
    public static TimeSpan NextRunDelay(TimeSpan runAt, DateTime utcNow)
    {
        var todayRun = utcNow.Date + runAt;
        var next = utcNow <= todayRun ? todayRun : todayRun.AddDays(1);
        return next - utcNow;
    }

    // Resolve the due customers and enqueue a job for each. Returns the count enqueued.
    public static async Task<int> EnqueueDueAsync(IServiceProvider sp, int threshold, CancellationToken ct)
    {
        var customers = sp.GetRequiredService<ICustomerRepository>();
        var queue = sp.GetRequiredService<IHarvestQueue>();
        var ids = await customers.IdsDueForHarvestAsync(threshold, ct);
        foreach (var id in ids) queue.Enqueue(new HarvestJob(id));
        return ids.Count;
    }
}

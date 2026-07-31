using Coldstart.Infrastructure.Harvest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Retention;

public sealed class RetentionOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan RunAtUtc { get; set; } = new(2, 30, 0);   // quiet hours, before the harvest
}

// Fires once per day and runs the retention sweep (see RetentionSweeper for the policy).
public sealed class RetentionCleanupService(
    IServiceScopeFactory scopes,
    IOptions<RetentionOptions> options,
    ILogger<RetentionCleanupService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled) { log.LogInformation("Retention cleanup disabled."); return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NightlyHarvestScheduler.NextRunDelay(opts.RunAtUtc, DateTime.UtcNow);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                using var scope = scopes.CreateScope();
                var sweeper = scope.ServiceProvider.GetRequiredService<RetentionSweeper>();
                await sweeper.SweepAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Retention sweep failed.");
            }
        }
    }
}

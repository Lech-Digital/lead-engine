using Coldstart.Application.Billing;
using Coldstart.Infrastructure.Harvest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coldstart.Infrastructure.Billing;

// Fires once per day (quiet hours) and downgrades lapsed complimentary trials.
public sealed class ComplimentaryExpiryService(
    IServiceScopeFactory scopes,
    ILogger<ComplimentaryExpiryService> log) : BackgroundService
{
    private static readonly TimeSpan RunAtUtc = new(3, 0, 0);   // after the retention sweep (02:30)

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NightlyHarvestScheduler.NextRunDelay(RunAtUtc, DateTime.UtcNow);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                using var scope = scopes.CreateScope();
                var sweeper = scope.ServiceProvider.GetRequiredService<ComplimentaryExpirySweeper>();
                var count = await sweeper.SweepAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (count > 0) log.LogInformation("Complimentary sweep downgraded {Count} customer(s).", count);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Complimentary expiry sweep failed.");
            }
        }
    }
}

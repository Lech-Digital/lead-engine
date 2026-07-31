using Coldstart.Application.FollowUps;
using Coldstart.Infrastructure.Harvest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.FollowUps;

public sealed class FollowUpOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan RunAtUtc { get; set; } = new(9, 0, 0);   // 09:00 UTC ≈ late morning NL
}

// Fires once per day at RunAtUtc and lets the use case decide who gets a nudge.
public sealed class TrialFollowUpScheduler(
    IServiceScopeFactory scopes,
    IOptions<FollowUpOptions> options,
    ILogger<TrialFollowUpScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled) { log.LogInformation("Trial follow-up scheduler disabled."); return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NightlyHarvestScheduler.NextRunDelay(opts.RunAtUtc, DateTime.UtcNow);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                using var scope = scopes.CreateScope();
                var sendFollowUps = scope.ServiceProvider.GetRequiredService<ISendTrialFollowUps>();
                var sent = await sendFollowUps.RunAsync(DateTimeOffset.UtcNow, stoppingToken);
                log.LogInformation("Trial follow-ups: sent {Count} nudge(s).", sent);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Trial follow-up run failed.");
            }
        }
    }
}

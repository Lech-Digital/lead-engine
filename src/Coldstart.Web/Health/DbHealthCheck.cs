using Coldstart.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Coldstart.Web.Health;

// Liveness for monitors and the container healthcheck: can we reach Postgres?
internal sealed class DbHealthCheck(ColdstartDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("database unreachable");
}

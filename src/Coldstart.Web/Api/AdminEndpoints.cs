using Coldstart.Application.Ports;
using Coldstart.Web.Admin;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Coldstart.Web.Api;

internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").AddEndpointFilter<AdminApiKeyFilter>();

        group.MapGet("/ping", () => Results.Ok(new { status = "ok" }));

        group.MapGet("/customers", async (
            ICustomerRepository customers, AdminUserService admin, CancellationToken ct) =>
        {
            var all = await customers.AllAsync(ct);
            var counts = await customers.LeadCountsAsync(ct);
            var userRows = await admin.ListAsync(actorEmail: null, ct);

            var dtos = all.Select(c =>
            {
                counts.TryGetValue(c.Id.Value, out var lc);
                return new AdminCustomerDto(
                    c.Id.Value, c.Name, c.Plan.ToString(), c.PlanRenewsAt,
                    c.StripeCustomerId is not null, c.StripeSubscriptionId is not null,
                    c.HarvestStatus.ToString(), c.LastHarvestAt,
                    lc?.Total ?? 0, lc?.PendingReview ?? 0,
                    userRows.Where(u => u.CustomerId == c.Id.Value)
                        .Select(u => new AdminUserDto(u.Email, u.Role, u.Status, u.DaysLeft))
                        .ToList());
            }).ToList();
            return Results.Ok(dtos);
        });

        group.MapGet("/stats", async (
            HealthCheckService health, ICustomerRepository customers, AdminUserService admin,
            CancellationToken ct) =>
        {
            var report = await health.CheckHealthAsync(ct);
            var all = await customers.AllAsync(ct);
            var rows = await admin.ListAsync(actorEmail: null, ct);
            return Results.Ok(new AdminStatsDto(
                report.Status.ToString(),
                all.Count,
                rows.Count,
                rows.Count(r => r.Status == "Pending"),
                rows.Count(r => r.Status == "Blocked"),
                all.GroupBy(c => c.HarvestStatus.ToString()).ToDictionary(g => g.Key, g => g.Count())));
        });

        return app;
    }
}

using Coldstart.Application.Harvest;
using Coldstart.Application.Ports;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Harvest;
using Coldstart.Web.Admin;
using Coldstart.Web.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

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
                        .ToList(),
                    c.ComplimentaryUntil);
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

        group.MapPost("/invites", async (InviteRequestDto body, AdminUserService admin, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Company)
                || string.IsNullOrWhiteSpace(body.Niche))
                return Results.Problem(statusCode: 400, title: "email, company and niche are required.");

            var result = await admin.InviteAsync(new InviteRequest(body.Email, body.Company, body.Niche, body.Info), ct);
            return result.Succeeded
                ? Results.Created("/api/admin/customers",
                    new { customerId = result.CustomerId, setPasswordLink = result.SetPasswordLink, message = result.Message })
                : ToProblem(result);
        });

        group.MapPost("/invites/{email}/resend", async (string email, AdminUserService admin, CancellationToken ct) =>
            ToHttp(await admin.ResendAsync(email, ct)));

        group.MapDelete("/invites/{email}", async (string email, AdminUserService admin, CancellationToken ct) =>
            ToHttp(await admin.RevokeAsync(email, ct)));

        group.MapPost("/customers/{id:guid}/complimentary", async (
            Guid id, ComplimentaryRequestDto body,
            Coldstart.Application.Billing.IGrantComplimentaryAccess grant,
            ColdstartDbContext db, Coldstart.Application.Ports.IEmailSender email,
            IOptions<Coldstart.Infrastructure.Notifications.NotificationsOptions> notifications,
            ILogger<Program> log, CancellationToken ct) =>
        {
            var result = await grant.ExecuteAsync(id, body.Days, ct);
            if (!result.Succeeded)
                return result.Message.Contains("No such customer")
                    ? Results.Problem(statusCode: 404, title: result.Message)
                    : Results.Problem(statusCode: 400, title: result.Message);

            // Notify the customer's confirmed account address; the grant stands even if mail fails.
            var emailSent = false;
            var to = await db.Users.IgnoreQueryFilters()
                .Where(u => u.CustomerId == id && u.Email != null && u.EmailConfirmed)
                .OrderBy(u => u.Email).Select(u => u.Email!).FirstOrDefaultAsync(ct);
            if (to is not null)
            {
                var appUrl = $"{notifications.Value.BaseUrl.TrimEnd('/')}/app";
                var content = EmailTemplates.ComplimentaryGranted(result.CustomerName!, result.Until!.Value, appUrl);
                try
                {
                    await email.SendAsync(to, content.Subject, content.TextBody, content.HtmlBody, ct);
                    emailSent = true;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    log.LogError(ex, "Complimentary grant email to {Email} failed.", to);
                }
            }
            return Results.Ok(new { message = result.Message, until = result.Until, emailSent });
        });

        group.MapPost("/users/{email}/block", async (string email, AdminUserService admin, CancellationToken ct) =>
            ToHttp(await admin.BlockAsync(email, actorEmail: null, ct)));

        group.MapPost("/users/{email}/unblock", async (string email, AdminUserService admin, CancellationToken ct) =>
            ToHttp(await admin.UnblockAsync(email, actorEmail: null, ct)));

        group.MapDelete("/users/{email}", async (string email, AdminUserService admin, CancellationToken ct) =>
            ToHttp(await admin.RemoveAsync(email, actorEmail: null, ct)));

        group.MapPost("/harvest/{customerId:guid}", async (
            Guid customerId, ICustomerRepository customers, IHarvestQueue queue, CancellationToken ct) =>
        {
            var customer = (await customers.AllAsync(ct)).FirstOrDefault(c => c.Id.Value == customerId);
            if (customer is null)
                return Results.Problem(statusCode: 404, title: "No such customer.");

            queue.Enqueue(new HarvestJob(customerId));
            return Results.Accepted($"/api/admin/customers", new { enqueued = customer.Name });
        });

        group.MapPost("/harvest/due", async (
            IServiceProvider services, IOptions<HarvestScheduleOptions> schedule, CancellationToken ct) =>
        {
            var count = await NightlyHarvestScheduler.EnqueueDueAsync(services, schedule.Value.TopUpThreshold, ct);
            return Results.Ok(new { enqueued = count });
        });

        return app;
    }

    private static IResult ToHttp(AdminActionResult result) =>
        result.Succeeded
            ? Results.Ok(new { message = result.Message, setPasswordLink = result.SetPasswordLink })
            : ToProblem(result);

    private static IResult ToProblem(AdminActionResult result) => result.Outcome switch
    {
        AdminOutcome.NotFound => Results.Problem(statusCode: 404, title: result.Message),
        AdminOutcome.Forbidden => Results.Problem(statusCode: 403, title: result.Message),
        _ => Results.Problem(statusCode: 409, title: result.Message),
    };
}

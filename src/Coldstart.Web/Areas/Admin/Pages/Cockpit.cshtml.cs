using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Web.Areas.Admin.Pages;

// The operator's morning glance: business vitals + per-customer engine health on one page.
[Authorize(Roles = ColdstartRoles.SuperAdmin)]
internal sealed class CockpitModel(ColdstartDbContext db) : PageModel
{
    public sealed record CustomerRow(
        string Name, string Plan, string HarvestStatus, DateTimeOffset? LastHarvestAt,
        int TotalLeads, int PendingLeads, int SentLeads, int Outcomes,
        DateTimeOffset? RetentionDeadline, bool Alarm);

    public int PayingCustomers { get; private set; }
    public int FoundThisWeek { get; private set; }
    public int SentThisWeek { get; private set; }
    public int OutcomesThisWeek { get; private set; }
    public int InquiriesThisWeek { get; private set; }
    public int RunsThisWeek { get; private set; }
    public int SignalsReadThisWeek { get; private set; }
    public int Exclusions { get; private set; }
    public IReadOnlyList<CustomerRow> Customers { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-7);

        PayingCustomers = await db.Customers.IgnoreQueryFilters().CountAsync(c => c.Plan == CustomerPlan.Full, ct);
        FoundThisWeek = await db.Leads.IgnoreQueryFilters().CountAsync(l => l.CreatedAt >= since && l.Status != LeadStatus.Rejected, ct);
        SentThisWeek = await db.Leads.IgnoreQueryFilters().CountAsync(l => l.SentAt >= since, ct);
        OutcomesThisWeek = await db.Leads.IgnoreQueryFilters().CountAsync(l => l.OutcomeAt >= since && l.Outcome != LeadOutcome.None, ct);
        InquiriesThisWeek = await db.Inquiries.CountAsync(i => i.CreatedAt >= since, ct);
        Exclusions = await db.Suppressions.CountAsync(ct);
        var runs = await db.HarvestRunStats.Where(s => s.RanAt >= since)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Read = g.Sum(s => s.Harvested) }).FirstOrDefaultAsync(ct);
        RunsThisWeek = runs?.Count ?? 0;
        SignalsReadThisWeek = runs?.Read ?? 0;

        var customers = await db.Customers.IgnoreQueryFilters().OrderBy(c => c.Name).ToListAsync(ct);
        var leadStats = await db.Leads.IgnoreQueryFilters()
            .GroupBy(l => l.CustomerId)
            .Select(g => new
            {
                Id = g.Key,
                Total = g.Count(),
                Pending = g.Count(l => l.Status == LeadStatus.PendingReview || l.Status == LeadStatus.Approved),
                Sent = g.Count(l => l.Status == LeadStatus.Sent),
                Outcomes = g.Count(l => l.Outcome != LeadOutcome.None),
            })
            .ToListAsync(ct);
        var byId = leadStats.ToDictionary(s => s.Id.Value);

        Customers = customers.Select(c =>
        {
            byId.TryGetValue(c.Id.Value, out var s);
            // Red flags: a failed harvest, or a paying customer whose engine hasn't run in 36h.
            var stale = c.Plan == CustomerPlan.Full &&
                        (c.LastHarvestAt is null || c.LastHarvestAt < DateTimeOffset.UtcNow.AddHours(-36));
            return new CustomerRow(
                c.Name, c.Plan.ToString(), c.HarvestStatus.ToString(), c.LastHarvestAt,
                s?.Total ?? 0, s?.Pending ?? 0, s?.Sent ?? 0, s?.Outcomes ?? 0,
                c.SubscriptionEndedAt?.AddDays(60),
                Alarm: c.HarvestStatus == HarvestStatus.Failed || stale);
        }).ToList();
    }
}

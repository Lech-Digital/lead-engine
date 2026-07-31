using Coldstart.Application.Leads;
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.FollowUps;

// Daily pass over Free-plan customers whose sample leads landed a while ago and who
// never subscribed. At most two nudges per customer, ever: day-2 and day-5 (relative
// to their harvest). Subscribing, opting out, or losing the Free plan stops everything.
internal sealed class SendTrialFollowUps(
    ICustomerRepository customers,
    ITenantScope tenants,
    IListLeads listLeads,
    QuotaOptions quota,
    ITrialFollowUpSender sender) : ISendTrialFollowUps
{
    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var candidates = await customers.TrialFollowUpCandidatesAsync(now.AddDays(-2), ct);
        var sent = 0;
        foreach (var candidate in candidates)
        {
            var stage = DueStage(candidate.LastHarvestAt, candidate.FollowUpStage, now);
            if (stage == TrialFollowUpStage.None) continue;

            // Lead listing runs under the tenant's scope like every other per-customer read.
            tenants.SetCurrent(candidate.Id);
            var customer = await customers.GetAsync(new CustomerId(candidate.Id), ct);
            if (customer is null || customer.Plan != CustomerPlan.Free || customer.FollowUpsOptOut) continue;

            var leads = await listLeads.ForCustomerAsync(customer.Id, customer.Plan, quota.FreeLeadCount, ct);
            var locked = leads.Count(l => l.Locked);

            if (!await sender.SendAsync(new TrialFollowUp(candidate.Id, stage, customer.Name, locked), ct))
                continue;

            customer.MarkFollowUpSent(stage);
            await customers.UpdateAsync(customer, ct);
            sent++;
        }
        return sent;
    }

    // Which nudge is due now, given what was already sent. A customer discovered late
    // (5+ days old, nothing sent) gets only the day-5 email — never two in one run.
    public static int DueStage(DateTimeOffset leadsArrivedAt, int alreadySent, DateTimeOffset now)
    {
        var age = now - leadsArrivedAt;
        if (age >= TimeSpan.FromDays(5) && alreadySent < TrialFollowUpStage.Day5) return TrialFollowUpStage.Day5;
        if (age >= TimeSpan.FromDays(2) && alreadySent < TrialFollowUpStage.Day2) return TrialFollowUpStage.Day2;
        return TrialFollowUpStage.None;
    }
}

using Coldstart.Domain;

namespace Coldstart.Application.Leads;

// Which of a customer's leads are locked behind the free-tier foil. Stable, ungameable ranking:
// Fit asc (Yes<Maybe<No), Score desc, CreatedAt asc, Id asc — computed over ALL leads (any status),
// so acting on a free lead never promotes a locked one. Full plan locks nothing.
public static class LeadLocking
{
    public static HashSet<Guid> LockedLeadIds(IReadOnlyList<Lead> leads, CustomerPlan plan, int freeCount)
    {
        if (plan == CustomerPlan.Full || freeCount < 0) return [];
        return leads
            .OrderBy(l => l.Fit)
            .ThenByDescending(l => l.Score)
            .ThenBy(l => l.CreatedAt)
            .ThenBy(l => l.Id.Value)
            .Skip(freeCount)
            .Select(l => l.Id.Value)
            .ToHashSet();
    }
}

using Coldstart.Domain;

namespace Coldstart.Application.Leads;

public sealed record LeadInbox(
    string CustomerName,
    IReadOnlyList<LeadSummary> Leads,
    CustomerPlan Plan,
    int LockedCount,
    HarvestStatus HarvestStatus);

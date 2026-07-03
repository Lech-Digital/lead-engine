namespace Coldstart.Application.Leads;

public sealed record LeadInbox(string CustomerName, IReadOnlyList<LeadSummary> Leads);

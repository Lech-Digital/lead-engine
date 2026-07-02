using Coldstart.Domain;

namespace Coldstart.Application.Leads;

public interface IListLeads
{
    Task<IReadOnlyList<LeadSummary>> ForCustomerAsync(CustomerId customerId, CancellationToken ct);
}

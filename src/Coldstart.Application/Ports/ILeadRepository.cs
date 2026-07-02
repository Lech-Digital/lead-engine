using Coldstart.Domain;

namespace Coldstart.Application.Ports;

public interface ILeadRepository
{
    Task<IReadOnlyList<Lead>> ForCustomerAsync(CustomerId customerId, CancellationToken ct);
    Task AddAsync(Lead lead, CancellationToken ct);
}

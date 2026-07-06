using Coldstart.Domain;

namespace Coldstart.Application.Ports;

public interface ICustomerRepository
{
    Task<IReadOnlyList<Customer>> AllAsync(CancellationToken ct);
    Task AddAsync(Customer customer, CancellationToken ct);
    Task<Customer?> GetByNameAsync(string name, CancellationToken ct);
}

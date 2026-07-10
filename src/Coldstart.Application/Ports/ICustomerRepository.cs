using Coldstart.Domain;

namespace Coldstart.Application.Ports;

public interface ICustomerRepository
{
    Task<IReadOnlyList<Customer>> AllAsync(CancellationToken ct);
    Task AddAsync(Customer customer, CancellationToken ct);
    Task<Customer?> GetByNameAsync(string name, CancellationToken ct);
    Task<Customer?> GetAsync(CustomerId id, CancellationToken ct);
    Task UpdateAsync(Customer customer, CancellationToken ct);
    Task DeleteAsync(CustomerId id, CancellationToken ct);
}

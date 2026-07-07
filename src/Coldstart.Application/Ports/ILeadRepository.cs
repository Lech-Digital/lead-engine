using Coldstart.Domain;

namespace Coldstart.Application.Ports;

public interface ILeadRepository
{
    Task<IReadOnlyList<Lead>> ForCustomerAsync(CustomerId customerId, CancellationToken ct);
    Task AddAsync(Lead lead, CancellationToken ct);
    Task<bool> ExistsByFingerprintAsync(string fingerprint, CancellationToken ct);
    Task<Lead?> GetAsync(LeadId id, CancellationToken ct);
    Task UpdateAsync(Lead lead, CancellationToken ct);
}

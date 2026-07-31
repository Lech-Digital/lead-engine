using Coldstart.Domain;

namespace Coldstart.Application.Ports;

// Global (cross-tenant) objection list — no tenant filter applies.
public interface ISuppressionRepository
{
    Task<IReadOnlyList<Suppression>> AllAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> ValuesAsync(CancellationToken ct);
    Task AddAsync(Suppression suppression, CancellationToken ct);
    Task RemoveAsync(Guid id, CancellationToken ct);
}

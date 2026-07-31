using Coldstart.Application.Ports;
using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Infrastructure.Repositories;

internal sealed class SuppressionRepository(ColdstartDbContext db) : ISuppressionRepository
{
    public async Task<IReadOnlyList<Suppression>> AllAsync(CancellationToken ct) =>
        await db.Suppressions.OrderByDescending(s => s.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<string>> ValuesAsync(CancellationToken ct) =>
        await db.Suppressions.Select(s => s.Value).ToListAsync(ct);

    public async Task AddAsync(Suppression suppression, CancellationToken ct)
    {
        // Idempotent: objecting twice is a no-op, not a crash.
        if (await db.Suppressions.AnyAsync(s => s.Value == suppression.Value, ct)) return;
        db.Suppressions.Add(suppression);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct) =>
        await db.Suppressions.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
}

using Coldstart.Application.Ports;
using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Infrastructure.Repositories;

internal sealed class LeadRepository(ColdstartDbContext db) : ILeadRepository
{
    public async Task<IReadOnlyList<Lead>> ForCustomerAsync(CustomerId customerId, CancellationToken ct) =>
        await db.Leads
            .Where(l => l.CustomerId == customerId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(Lead lead, CancellationToken ct)
    {
        await db.Leads.AddAsync(lead, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> ExistsByFingerprintAsync(string fingerprint, CancellationToken ct) =>
        await db.Leads.AnyAsync(l => l.Fingerprint == fingerprint && fingerprint != "", ct);

    public async Task<Lead?> GetAsync(LeadId id, CancellationToken ct) =>
        await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task UpdateAsync(Lead lead, CancellationToken ct)
    {
        db.Leads.Update(lead);
        await db.SaveChangesAsync(ct);
    }
}

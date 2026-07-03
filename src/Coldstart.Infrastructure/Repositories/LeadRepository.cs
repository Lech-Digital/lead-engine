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
}

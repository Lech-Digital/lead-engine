using Coldstart.Application.Ports;
using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Infrastructure.Repositories;

internal sealed class InquiryRepository(ColdstartDbContext db) : IInquiryRepository
{
    public async Task AddAsync(Inquiry inquiry, CancellationToken ct)
    {
        await db.Inquiries.AddAsync(inquiry, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Inquiry>> RecentAsync(int max, CancellationToken ct) =>
        await db.Inquiries.OrderByDescending(i => i.CreatedAt).Take(max).ToListAsync(ct);

    public async Task<IReadOnlyList<Inquiry>> AllAsync(CancellationToken ct) =>
        await db.Inquiries.OrderByDescending(i => i.CreatedAt).ToListAsync(ct);

    public async Task<Inquiry?> GetAsync(InquiryId id, CancellationToken ct) =>
        await db.Inquiries.FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task UpdateAsync(Inquiry inquiry, CancellationToken ct)
    {
        db.Inquiries.Update(inquiry);
        await db.SaveChangesAsync(ct);
    }
}

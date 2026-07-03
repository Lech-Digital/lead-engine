using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Infrastructure.Repositories;

internal sealed class InquiryRepository(ColdstartDbContext db) : IInquiryRepository
{
    public async Task AddAsync(Inquiry inquiry, CancellationToken ct)
    {
        await db.Inquiries.AddAsync(inquiry, ct);
        await db.SaveChangesAsync(ct);
    }
}

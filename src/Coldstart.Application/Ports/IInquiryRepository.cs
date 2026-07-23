using Coldstart.Domain;

namespace Coldstart.Application.Ports;

public interface IInquiryRepository
{
    Task AddAsync(Inquiry inquiry, CancellationToken ct);
    Task<IReadOnlyList<Inquiry>> RecentAsync(int max, CancellationToken ct);
    Task<Inquiry?> GetAsync(InquiryId id, CancellationToken ct);
}

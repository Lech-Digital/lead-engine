using Coldstart.Domain;

namespace Coldstart.Application.Ports;

public interface IInquiryRepository
{
    Task AddAsync(Inquiry inquiry, CancellationToken ct);
    Task<IReadOnlyList<Inquiry>> RecentAsync(int max, CancellationToken ct);
    // Every inquiry, newest first — the admin lifecycle view needs the full backlog (open,
    // converted and dismissed alike), not just a capped recent slice.
    Task<IReadOnlyList<Inquiry>> AllAsync(CancellationToken ct);
    Task<Inquiry?> GetAsync(InquiryId id, CancellationToken ct);
    Task UpdateAsync(Inquiry inquiry, CancellationToken ct);
}

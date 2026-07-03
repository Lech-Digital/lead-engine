using Coldstart.Domain;

namespace Coldstart.Application.Ports;

public interface IInquiryRepository
{
    Task AddAsync(Inquiry inquiry, CancellationToken ct);
}

namespace Coldstart.Application.Leads;

public interface IGetLeadDetail
{
    Task<LeadDetail?> ForLeadAsync(Guid customerId, Guid leadId, CancellationToken ct);
}

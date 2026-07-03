namespace Coldstart.Application.Leads;

public interface IGetLeadInbox
{
    Task<LeadInbox> ForCustomerAsync(Guid customerId, CancellationToken ct);
}

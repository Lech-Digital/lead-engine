namespace Coldstart.Application.Leads;

public interface IGetLeadInbox
{
    Task<LeadInbox> ForFirstCustomerAsync(CancellationToken ct);
}

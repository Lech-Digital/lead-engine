namespace Coldstart.Application.Harvest;

public interface IHarvestLeads
{
    Task<HarvestReport> RunAsync(CancellationToken ct);
    Task<HarvestReport> RunForCustomerAsync(Guid customerId, HarvestTargeting targeting, CancellationToken ct);
}

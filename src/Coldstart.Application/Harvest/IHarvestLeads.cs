namespace Coldstart.Application.Harvest;

public interface IHarvestLeads
{
    Task<HarvestReport> RunAsync(CancellationToken ct);
}

namespace Coldstart.Application.Harvest;

public interface ILeadEnricher
{
    Task<EnrichmentResult?> EnrichAsync(RawItem item, NicheOptions niche, SignalResult signal, CancellationToken ct);
}

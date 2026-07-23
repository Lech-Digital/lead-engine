namespace Coldstart.Application.Harvest;

public sealed record HarvestJob(Guid CustomerId);

public interface IHarvestQueue
{
    void Enqueue(HarvestJob job);
}

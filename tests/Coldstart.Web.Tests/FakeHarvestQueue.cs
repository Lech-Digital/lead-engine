using Coldstart.Application.Harvest;

namespace Coldstart.Web.Tests;

// Records enqueued jobs without running a harvest (keeps invites offline in tests).
public sealed class FakeHarvestQueue : IHarvestQueue
{
    public List<HarvestJob> Jobs { get; } = [];

    public void Enqueue(HarvestJob job) => Jobs.Add(job);
}

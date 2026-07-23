using System.Threading.Channels;
using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

internal sealed class HarvestQueue : IHarvestQueue
{
    private readonly Channel<HarvestJob> _channel = Channel.CreateUnbounded<HarvestJob>();

    public ChannelReader<HarvestJob> Reader => _channel.Reader;

    public void Enqueue(HarvestJob job) => _channel.Writer.TryWrite(job);
}

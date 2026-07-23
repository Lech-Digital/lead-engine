using Coldstart.Application.Harvest;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class HarvestQueueTests
{
    [Fact]
    public async Task Enqueue_MakesJobsReadable_InFifoOrder()
    {
        // Arrange
        var queue = new HarvestQueue();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        // Act
        queue.Enqueue(new HarvestJob(a));
        queue.Enqueue(new HarvestJob(b));

        // Assert — both readable, in order
        var read = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var job = await queue.Reader.ReadAsync(CancellationToken.None);
            read.Add(job.CustomerId);
        }
        read.Should().Equal(a, b);
    }
}

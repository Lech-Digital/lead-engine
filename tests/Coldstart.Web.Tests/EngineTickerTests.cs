using Coldstart.Domain;
using Coldstart.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public class EngineTickerTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Landing_WithRecentRunStats_ShowsTheTicker()
    {
        // Arrange
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
            db.HarvestRunStats.Add(HarvestRunStat.Record(DateTimeOffset.UtcNow.AddDays(-1), 1234, 9));
            await db.SaveChangesAsync();
            // the ticker caches for an hour — flush so this test sees fresh data
            (factory.Services.GetRequiredService<IMemoryCache>() as MemoryCache)?.Clear();
        }
        var client = factory.CreateClient();

        // Act
        var html = await client.GetStringAsync("/");

        // Assert
        html.Should().Contain("the engine read");
        html.Should().Contain("1,234");
    }

    [Fact]
    public void Record_CapturesCounts()
    {
        // Arrange & Act
        var stat = HarvestRunStat.Record(DateTimeOffset.UtcNow, 77, 3);

        // Assert
        stat.Harvested.Should().Be(77);
        stat.Saved.Should().Be(3);
        stat.Id.Should().NotBeEmpty();
    }
}

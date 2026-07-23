using Coldstart.Application.Harvest;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class HarvestEndToEndTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private sealed class OneSource(RawItem item) : ILeadSource
    {
        public string Name => "fake"; public double BaseScore => 0; public double ScoreWeight => 1; public bool Enabled => true;
        public Task<IReadOnlyList<RawItem>> FetchAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<RawItem>>([item]);
    }

    [Fact]
    public async Task Run_PersistsFilteredLead_UnderHouseCustomer_AndDedupsSecondRun()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        var niche = new NicheOptions { TargetKeywords = ["outbound"], BuyingSignals = ["hiring"], MinScore = 0.4, MaxEnrichPerRun = 5 };
        var item = new RawItem("fake", "e2e-1", "", "hiring outbound SDR", "", "https://x/1", "");
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "sum", "subj", "draft", "Acme"));
        HarvestLeads Sut(ColdstartDbContext ctx) =>
            new([new OneSource(item)], enricher, new LeadRepository(ctx), new CustomerRepository(ctx), niche);

        // Act
        var first = await Sut(db).RunAsync(CancellationToken.None);
        await using var db2 = fixture.NewDbContext();
        var second = await Sut(db2).RunAsync(CancellationToken.None);

        // Assert
        first.Saved.Should().Be(1);
        second.Saved.Should().Be(0);   // fingerprint dedup on the persisted lead
        await using var read = fixture.NewDbContext();
        var lead = await read.Leads.SingleAsync(l => l.Fingerprint == item.Fingerprint);
        lead.Company.Should().Be("Acme");
        var house = await read.Customers.SingleAsync(c => c.Name == "Coldstart");
        lead.CustomerId.Should().Be(house.Id);
    }
}

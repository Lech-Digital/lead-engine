using Coldstart.Application.Harvest;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure.Repositories;
using Coldstart.Infrastructure.Tenancy;
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
        public Task<IReadOnlyList<RawItem>> FetchAsync(NicheOptions runNiche, CancellationToken ct) => Task.FromResult<IReadOnlyList<RawItem>>([item]);
    }

    // The DbContext's tenant filter reads from the same ITenantContext instance HarvestLeads sets
    // via ITenantScope (this is how the real DI composition wires them — see DependencyInjection.cs).
    // fixture.NewDbContext() mints its own private tenant holder, so this test builds the context
    // directly against a tenant instance it shares with HarvestLeads, or the fail-closed filter
    // would hide the customer/leads HarvestLeads just resolved and wrote.
    private ColdstartDbContext NewSharedDbContext(MutableTenantContext tenant)
    {
        var options = new DbContextOptionsBuilder<ColdstartDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        return new ColdstartDbContext(options, tenant);
    }

    [Fact]
    public async Task Run_PersistsFilteredLead_UnderHouseCustomer_AndDedupsSecondRun()
    {
        // Arrange
        var tenantScope = new MutableTenantContext();
        await using var db = NewSharedDbContext(tenantScope);
        var niche = new NicheOptions { TargetKeywords = ["outbound"], BuyingSignals = ["hiring"], MinScore = 0.4, MaxEnrichPerRun = 5 };
        var item = new RawItem("fake", "e2e-1", "", "hiring outbound SDR", "", "https://x/1", "");
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "sum", "subj", "draft", "Acme"));
        var lookup = Substitute.For<ISecondaryContextLookup>();
        lookup.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        HarvestLeads Sut(ColdstartDbContext ctx, MutableTenantContext scope) =>
            new([new OneSource(item)], enricher, lookup, new LeadRepository(ctx), new CustomerRepository(ctx), new SuppressionRepository(ctx), niche, scope);

        // Act
        var first = await Sut(db, tenantScope).RunAsync(CancellationToken.None);
        var tenantScope2 = new MutableTenantContext();
        await using var db2 = NewSharedDbContext(tenantScope2);
        var second = await Sut(db2, tenantScope2).RunAsync(CancellationToken.None);

        // Assert
        first.Saved.Should().Be(1);
        second.Saved.Should().Be(0);   // fingerprint dedup on the persisted lead
        // System-level verification read, not a tenant-scoped one — bypass the filter deliberately.
        await using var read = fixture.NewDbContext();
        var lead = await read.Leads.IgnoreQueryFilters().SingleAsync(l => l.Fingerprint == item.Fingerprint);
        lead.Company.Should().Be("Acme");
        var house = await read.Customers.IgnoreQueryFilters().SingleAsync(c => c.Name == "Coldstart");
        lead.CustomerId.Should().Be(house.Id);
        // The tenant scope is set before dedup/save, so it reflects the resolved target customer.
        ((ITenantContext)tenantScope).CustomerId.Should().Be(house.Id.Value);
    }
}

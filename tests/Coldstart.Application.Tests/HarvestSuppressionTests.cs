using Coldstart.Application.Harvest;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class HarvestSuppressionTests
{
    private static NicheOptions Niche() => new()
    {
        TargetKeywords = ["outbound"], BuyingSignals = ["hiring"], MinScore = 0.4, MaxEnrichPerRun = 10,
    };

    private sealed class FakeSource(params RawItem[] items) : ILeadSource
    {
        public string Name => "fake";
        public double BaseScore => 0;
        public double ScoreWeight => 1;
        public bool Enabled => true;
        public Task<IReadOnlyList<RawItem>> FetchAsync(NicheOptions runNiche, CancellationToken ct) => Task.FromResult<IReadOnlyList<RawItem>>(items);
    }

    private static (ILeadRepository Leads, ICustomerRepository Customers, ISuppressionRepository Suppressions) Deps(params string[] suppressedValues)
    {
        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        var suppressions = Substitute.For<ISuppressionRepository>();
        suppressions.ValuesAsync(Arg.Any<CancellationToken>()).Returns(suppressedValues);
        return (leads, customers, suppressions);
    }

    private static ISecondaryContextLookup NoSecondaryContext()
    {
        var lookup = Substitute.For<ISecondaryContextLookup>();
        lookup.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        return lookup;
    }

    [Fact]
    public async Task RunAsync_SuppressedRawItem_NeverReachesTheEnricher()
    {
        // Arrange — the objecting company's name is in the item title
        var item = new RawItem("fake", "1", "Bluecore Energy raises €4M", "hiring outbound SDR", "", "u1", "");
        var enricher = Substitute.For<ILeadEnricher>();
        var (leads, customers, suppressions) = Deps("bluecore energy");
        var sut = new HarvestLeads([new FakeSource(item)], enricher, NoSecondaryContext(), leads, customers, suppressions, Niche(), Substitute.For<ITenantScope>());

        // Act
        var report = await sut.RunAsync(CancellationToken.None);

        // Assert — dropped before any LLM spend or persistence
        report.Saved.Should().Be(0);
        await enricher.DidNotReceive().EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await leads.DidNotReceive().AddAsync(Arg.Any<Lead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_SuppressedCompanyDiscoveredByEnrichment_IsNotSaved()
    {
        // Arrange — raw text is clean; only the enriched company name reveals the objector
        var item = new RawItem("fake", "1", "", "hiring outbound SDR", "", "u1", "");
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "s", "subj", "draft", "Bluecore Energy B.V."));
        var (leads, customers, suppressions) = Deps("bluecore energy");
        var sut = new HarvestLeads([new FakeSource(item)], enricher, NoSecondaryContext(), leads, customers, suppressions, Niche(), Substitute.For<ITenantScope>());

        // Act
        var report = await sut.RunAsync(CancellationToken.None);

        // Assert
        report.Saved.Should().Be(0);
        await leads.DidNotReceive().AddAsync(Arg.Any<Lead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_UnrelatedCompanies_AreUnaffectedBySuppressions()
    {
        // Arrange
        var item = new RawItem("fake", "1", "", "hiring outbound SDR", "", "u1", "");
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "s", "subj", "draft", "Acme"));
        var (leads, customers, suppressions) = Deps("bluecore energy");
        var sut = new HarvestLeads([new FakeSource(item)], enricher, NoSecondaryContext(), leads, customers, suppressions, Niche(), Substitute.For<ITenantScope>());

        // Act
        var report = await sut.RunAsync(CancellationToken.None);

        // Assert
        report.Saved.Should().Be(1);
        await leads.Received(1).AddAsync(Arg.Is<Lead>(l => l.Company == "Acme"), Arg.Any<CancellationToken>());
    }
}

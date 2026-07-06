using Coldstart.Application.Harvest;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class HarvestLeadsTests
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
        public Task<IReadOnlyList<RawItem>> FetchAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<RawItem>>(items);
    }

    [Fact]
    public async Task RunAsync_SavesOnlyAboveThresholdNonDuplicateFits()
    {
        // Arrange
        var good = new RawItem("fake", "1", "", "hiring outbound SDR", "", "u1", "");   // scores >= 0.4
        var weak = new RawItem("fake", "2", "", "unrelated chatter", "", "u2", "");       // below threshold
        var source = new FakeSource(good, weak);
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "s", "subj", "draft", "Acme"));
        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var customers = Substitute.For<ICustomerRepository>();
        customers.AllAsync(Arg.Any<CancellationToken>()).Returns(new List<Customer>());
        var sut = new HarvestLeads([source], enricher, leads, customers, Niche());

        // Act
        var report = await sut.RunAsync(CancellationToken.None);

        // Assert — only the good item is enriched + saved; weak dropped at the score gate
        report.Harvested.Should().Be(2);
        report.Saved.Should().Be(1);
        await leads.Received(1).AddAsync(Arg.Is<Lead>(l => l.Company == "Acme" && l.Fingerprint == good.Fingerprint), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_SkipsDuplicatesAndFitNo()
    {
        // Arrange
        var dup = new RawItem("fake", "1", "", "hiring outbound", "", "u1", "");
        var no = new RawItem("fake", "2", "", "hiring outbound", "", "u2", "");
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.No, "s", "-", "-", "-"));
        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(dup.Fingerprint, Arg.Any<CancellationToken>()).Returns(true);
        leads.ExistsByFingerprintAsync(no.Fingerprint, Arg.Any<CancellationToken>()).Returns(false);
        var customers = Substitute.For<ICustomerRepository>();
        customers.AllAsync(Arg.Any<CancellationToken>()).Returns(new List<Customer>());
        var sut = new HarvestLeads([new FakeSource(dup, no)], enricher, leads, customers, Niche());

        // Act
        var report = await sut.RunAsync(CancellationToken.None);

        // Assert — dup skipped before enrich; the other enriches to FIT:NO and is not saved
        report.Saved.Should().Be(0);
        await leads.DidNotReceive().AddAsync(Arg.Any<Lead>(), Arg.Any<CancellationToken>());

        // the duplicate must be skipped before any LLM spend; the non-dup is enriched
        await enricher.DidNotReceive().EnrichAsync(
            Arg.Is<RawItem>(i => i.Fingerprint == dup.Fingerprint), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<CancellationToken>());
        await enricher.Received(1).EnrichAsync(
            Arg.Is<RawItem>(i => i.Fingerprint == no.Fingerprint), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<CancellationToken>());
    }
}

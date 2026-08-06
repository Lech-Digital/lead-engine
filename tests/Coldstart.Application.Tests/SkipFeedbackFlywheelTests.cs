using Coldstart.Application.Harvest;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class SkipFeedbackFlywheelTests
{
    private sealed class FakeSource(params RawItem[] items) : ILeadSource
    {
        public string Name => "fake";
        public double BaseScore => 0;
        public double ScoreWeight => 1;
        public bool Enabled => true;
        public Task<IReadOnlyList<RawItem>> FetchAsync(NicheOptions runNiche, CancellationToken ct) => Task.FromResult<IReadOnlyList<RawItem>>(items);
    }

    private static ISecondaryContextLookup NoSecondaryContext()
    {
        var lookup = Substitute.For<ISecondaryContextLookup>();
        lookup.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        return lookup;
    }

    private static Lead SkippedLead(CustomerId cid, string company, string reason)
    {
        var lead = Lead.CreateHarvested(cid, company, Fit.Yes, "s", "d",
            "hackernews", "https://x", "fp-" + Guid.NewGuid().ToString("N"), 0.8, "outbound", DateTimeOffset.UtcNow);
        lead.Discard(reason);
        return lead;
    }

    [Fact]
    public async Task RunForCustomerAsync_FeedsRecentSkipReasonsToTheEnricher()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        customer.UpdateProfile(CompanyProfile.Empty with
        {
            TargetKeywords = "outbound", BuyingSignals = "hiring", Offer = "leads",
        });
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);

        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        leads.ForCustomerAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>())
            .Returns([SkippedLead(customer.Id, "MegaCorp", "Too big"), SkippedLead(customer.Id, "PetShop", "Wrong industry")]);

        SenderContext? seen = null;
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(),
                Arg.Do<SenderContext>(s => seen = s), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((EnrichmentResult?)null);

        var suppressions = Substitute.For<ISuppressionRepository>();
        suppressions.ValuesAsync(Arg.Any<CancellationToken>()).Returns([]);
        var sut = new HarvestLeads([new FakeSource(new RawItem("fake", "1", "", "hiring outbound SDR", "", "u1", ""))],
            enricher, NoSecondaryContext(), leads, customers, suppressions, new NicheOptions { MinScore = 0.1, MaxEnrichPerRun = 5 }, Substitute.For<ITenantScope>());

        // Act
        await sut.RunForCustomerAsync(customer.Id.Value, HarvestTargeting.FromNiche(new NicheOptions
        {
            TargetKeywords = ["outbound"], BuyingSignals = ["hiring"], MinScore = 0.1, MaxEnrichPerRun = 5,
        }), CancellationToken.None);

        // Assert — the enricher's sender context carries the learned preferences
        seen.Should().NotBeNull();
        seen!.SkipFeedback.Should().Contain("MegaCorp: Too big").And.Contain("PetShop: Wrong industry");
    }

    [Fact]
    public async Task RunForCustomerAsync_WithNoReasonedSkips_LeavesFeedbackNull()
    {
        // Arrange — skips without reasons teach nothing
        var customer = Customer.Create("Acme");
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);
        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var noReason = Lead.CreateHarvested(customer.Id, "Quiet BV", Fit.Yes, "s", "d",
            "hackernews", "https://x", "fp", 0.8, "outbound", DateTimeOffset.UtcNow);
        noReason.Discard();
        leads.ForCustomerAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns([noReason]);

        SenderContext? seen = null;
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(),
                Arg.Do<SenderContext>(s => seen = s), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((EnrichmentResult?)null);
        var suppressions = Substitute.For<ISuppressionRepository>();
        suppressions.ValuesAsync(Arg.Any<CancellationToken>()).Returns([]);
        var sut = new HarvestLeads([new FakeSource(new RawItem("fake", "1", "", "hiring outbound SDR", "", "u1", ""))],
            enricher, NoSecondaryContext(), leads, customers, suppressions, new NicheOptions(), Substitute.For<ITenantScope>());

        // Act
        await sut.RunForCustomerAsync(customer.Id.Value, HarvestTargeting.FromNiche(new NicheOptions
        {
            TargetKeywords = ["outbound"], BuyingSignals = ["hiring"], MinScore = 0.1, MaxEnrichPerRun = 5,
        }), CancellationToken.None);

        // Assert
        seen.Should().NotBeNull();
        seen!.SkipFeedback.Should().BeNull();
    }
}

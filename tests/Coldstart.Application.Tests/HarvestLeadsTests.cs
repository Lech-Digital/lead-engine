using Coldstart.Application.Harvest;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
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
        public Task<IReadOnlyList<RawItem>> FetchAsync(NicheOptions runNiche, CancellationToken ct) => Task.FromResult<IReadOnlyList<RawItem>>(items);
    }

    // Default: no secondary snippet available. Individual tests override when the
    // lookup's behaviour (success/throw) is the thing under test.
    private static ISecondaryContextLookup NoSecondaryContext()
    {
        var lookup = Substitute.For<ISecondaryContextLookup>();
        lookup.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        return lookup;
    }

    [Fact]
    public async Task RunAsync_RanksHigherSignalItemAboveWeakOne_AndEnrichesOnlyTheTopWithinBudget()
    {
        // Arrange — Task 3 removed the score gate: "weak" no longer gets dropped for scoring
        // low, it just ranks behind "good" and misses this run's enrich budget of 1.
        var good = new RawItem("fake", "1", "", "hiring outbound SDR", "", "u1", "");   // matches keywords, ranks high
        var weak = new RawItem("fake", "2", "", "unrelated chatter", "", "u2", "");       // no signal, ranks low
        var source = new FakeSource(good, weak);
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "s", "subj", "draft", "Acme"));
        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        Customer? target = null;
        customers.AddAsync(Arg.Do<Customer>(c => target = c), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var tenantScope = Substitute.For<ITenantScope>();
        var niche = new NicheOptions { TargetKeywords = ["outbound"], BuyingSignals = ["hiring"], MinScore = 0.4, MaxEnrichPerRun = 1 };
        var sut = new HarvestLeads([source], enricher, NoSecondaryContext(), leads, customers, NoSuppressions(), niche, tenantScope);

        // Act
        var report = await sut.RunAsync(CancellationToken.None);

        // Assert — both items are harvested; neither is dropped by a score gate (there is none
        // any more). "weak" is merely out-ranked and misses this run's budget of 1, it is not
        // rejected or vetoed.
        report.Harvested.Should().Be(2);
        report.DropReasons.Should().NotContainKey("below-score");
        report.Saved.Should().Be(1);
        await leads.Received(1).AddAsync(Arg.Is<Lead>(l => l.Company == "Acme" && l.Fingerprint == good.Fingerprint && l.CustomerId == target!.Id), Arg.Any<CancellationToken>());
        tenantScope.Received(1).SetCurrent(target!.Id.Value);
        await enricher.DidNotReceive().EnrichAsync(
            Arg.Is<RawItem>(i => i.Fingerprint == weak.Fingerprint), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenSameItemHarvestedTwiceInOneRun_DedupesWithinRun()
    {
        // Arrange — the same HN comment matched two search queries, so the source emits it
        // twice with an identical fingerprint (same Source:ExternalId). The DB doesn't know it yet.
        var first = new RawItem("fake", "1", "", "hiring outbound SDR", "", "u1", "");
        var again = new RawItem("fake", "1", "", "hiring outbound SDR", "", "u1", "");
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "s", "subj", "draft", "Acme"));
        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        var sut = new HarvestLeads([new FakeSource(first, again)], enricher, NoSecondaryContext(), leads, customers, NoSuppressions(), Niche(), Substitute.For<ITenantScope>());

        // Act
        var report = await sut.RunAsync(CancellationToken.None);

        // Assert — the duplicate is dropped in the harvest phase, so it never re-enriches
        // (no double LLM spend) and the unique-fingerprint save happens exactly once
        report.Harvested.Should().Be(2);
        report.Deduped.Should().Be(1);
        report.Saved.Should().Be(1);
        await enricher.Received(1).EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await leads.Received(1).AddAsync(Arg.Is<Lead>(l => l.Fingerprint == first.Fingerprint), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_SkipsDuplicatesAndFitNo()
    {
        // Arrange
        var dup = new RawItem("fake", "1", "", "hiring outbound", "", "u1", "");
        var no = new RawItem("fake", "2", "", "hiring outbound", "", "u2", "");
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.No, "s", "-", "-", "-"));
        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(dup.Fingerprint, Arg.Any<CancellationToken>()).Returns(true);
        leads.ExistsByFingerprintAsync(no.Fingerprint, Arg.Any<CancellationToken>()).Returns(false);
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        var sut = new HarvestLeads([new FakeSource(dup, no)], enricher, NoSecondaryContext(), leads, customers, NoSuppressions(), Niche(), Substitute.For<ITenantScope>());

        // Act
        var report = await sut.RunAsync(CancellationToken.None);

        // Assert — dup skipped before enrich; the other enriches to FIT:NO, not saved but
        // persisted as Rejected so its fingerprint is remembered (no re-enrich next run)
        report.Saved.Should().Be(0);
        report.Rejected.Should().Be(1);
        await leads.Received(1).AddAsync(
            Arg.Is<Lead>(l => l.Fingerprint == no.Fingerprint && l.Status == LeadStatus.Rejected), Arg.Any<CancellationToken>());

        // the duplicate must be skipped before any LLM spend; the non-dup is enriched
        await enricher.DidNotReceive().EnrichAsync(
            Arg.Is<RawItem>(i => i.Fingerprint == dup.Fingerprint), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await enricher.Received(1).EnrichAsync(
            Arg.Is<RawItem>(i => i.Fingerprint == no.Fingerprint), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_PassesTargetCustomerBriefAndToggle_ToTheEnricher()
    {
        // Arrange — seed the target customer WITH a profile brief + toggle via the fake ICustomerRepository;
        // the enricher fake captures the SenderContext it received via Arg.Do.
        var item = new RawItem("fake", "1", "", "hiring outbound SDR", "", "u1", "");
        var source = new FakeSource(item);
        SenderContext? captured = null;
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(),
                Arg.Do<SenderContext>(s => captured = s), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "s", "subj", "draft", "Acme"));
        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var target = Customer.Create("Acme Target");
        target.UpdateProfile(new CompanyProfile(null, null, null, null, null, null, null, "We help dentists.", true));
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(target);
        var sut = new HarvestLeads([source], enricher, NoSecondaryContext(), leads, customers, NoSuppressions(), Niche(), Substitute.For<ITenantScope>());

        // Act
        await sut.RunAsync(CancellationToken.None);

        // Assert
        captured!.Brief.Should().Be("We help dentists.");
        captured.MentionSourceTool.Should().BeTrue();
    }

    [Fact]
    public async Task RunForCustomerAsync_SavesEnrichedLeadsToTheGivenCustomer_UsingTargetingKeywords()
    {
        // Arrange — one source item that matches the targeting keyword; enricher returns a YES
        var cid = Guid.NewGuid();
        var customer = Customer.Create("Acme");
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(customer);

        // CreatedAt must stay within the veto stage's freshness window (relative to "now",
        // not a fixed calendar date, or this test becomes a time bomb).
        var item = new RawItem("hackernews", "1", "hiring a dentist", "we need a dentist now", "acme", "https://x/1", DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var source = Substitute.For<ILeadSource>();
        source.Enabled.Returns(true);
        source.BaseScore.Returns(0.0);
        source.ScoreWeight.Returns(1.0);
        source.FetchAsync(Arg.Any<NicheOptions>(), Arg.Any<CancellationToken>()).Returns(new[] { item });

        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "sum", "subj", "draft", "Acme"));

        var globalNiche = new NicheOptions();   // provides HackerNews/Funding source config
        var scope = Substitute.For<ITenantScope>();
        var sut = new HarvestLeads(new[] { source }, enricher, NoSecondaryContext(), leads, customers, NoSuppressions(), globalNiche, scope);
        var targeting = new HarvestTargeting("offer", ["dentist"], ["hiring"], [], "Acme", "", 0.4, 30);

        // Act
        var report = await sut.RunForCustomerAsync(cid, targeting, CancellationToken.None);

        // Assert — the run scoped to the customer and saved the lead under its id
        scope.Received().SetCurrent(cid);
        report.Saved.Should().Be(1);
        await leads.Received().AddAsync(Arg.Is<Lead>(l => l.CustomerId.Value == cid && l.Company == "Acme"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunForCustomerAsync_LookupSucceeds_PassesSecondaryContextToEnricher()
    {
        // Arrange — ISecondaryContextLookup substitute returns "extra snippet" for the item's author
        var cid = Guid.NewGuid();
        var customer = Customer.Create("Acme");
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(customer);
        var item = new RawItem("fake", "1", "", "hiring outbound SDR", "SomeCo", "u1", "");
        var source = new FakeSource(item);
        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "s", "subj", "draft", "Acme"));
        var lookup = Substitute.For<ISecondaryContextLookup>();
        lookup.LookupAsync("SomeCo", Arg.Any<CancellationToken>()).Returns("extra snippet");
        var sut = new HarvestLeads([source], enricher, lookup, leads, customers, NoSuppressions(), Niche(), Substitute.For<ITenantScope>());
        var targeting = new HarvestTargeting("offer", ["outbound"], ["hiring"], [], "Acme", "", 0.4, 10);

        // Act — run the pipeline
        await sut.RunForCustomerAsync(cid, targeting, CancellationToken.None);

        // Assert
        await enricher.Received().EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(),
            Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), "extra snippet", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunForCustomerAsync_LookupThrows_EnrichesWithNullContext()
    {
        // Arrange — lookup substitute throws
        var cid = Guid.NewGuid();
        var customer = Customer.Create("Acme");
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(customer);
        var item = new RawItem("fake", "1", "", "hiring outbound SDR", "SomeCo", "u1", "");
        var source = new FakeSource(item);
        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "s", "subj", "draft", "Acme"));
        var lookup = Substitute.For<ISecondaryContextLookup>();
        lookup.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));
        var sut = new HarvestLeads([source], enricher, lookup, leads, customers, NoSuppressions(), Niche(), Substitute.For<ITenantScope>());
        var targeting = new HarvestTargeting("offer", ["outbound"], ["hiring"], [], "Acme", "", 0.4, 10);

        // Act — run the pipeline
        var report = await sut.RunForCustomerAsync(cid, targeting, CancellationToken.None);

        // Assert — the batch is not aborted; enricher called with null secondary context
        report.Saved.Should().Be(1);
        await enricher.Received().EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(),
            Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), null, Arg.Any<CancellationToken>());
    }

    private static ISuppressionRepository NoSuppressions()
    {
        var repo = Substitute.For<ISuppressionRepository>();
        repo.ValuesAsync(Arg.Any<CancellationToken>()).Returns([]);
        return repo;
    }

    [Fact]
    public async Task RunAsync_RunWithSeveralDistinctDrops_AttributesEachToItsReason_AndKeepsLegacyCountersHonest()
    {
        // Arrange — seven raw items, each engineered to hit exactly one funnel decision point:
        //   suppressedItem              -> "suppressed" (pre-score GDPR check)
        //   weakItem                    -> no signal at all, but Task 3 removed the score gate,
        //                                  so it now reaches the (unconfigured) enricher fake,
        //                                  which returns null — the ordinary "LLM empty" path,
        //                                  not a drop reason
        //   dupDbItem                   -> "duplicate-fingerprint" (DB check)
        //   dupRunItem1 + dupRunItem2   -> the second is "duplicate-fingerprint" (in-run HashSet)
        //   goodItem                    -> saved
        //   postEnrichSuppressedItem    -> "suppressed-after-enrichment" (post-enrichment gate)
        // dupRunItem1 also reaches enrichment and is saved, so the funnel sees several distinct
        // reasons AND a reason hit twice in the same run — the coincidence the earlier test missed.
        var suppressedItem = new RawItem("fake", "supp", "BlockedCo raises funding", "hiring outbound SDR", "u1", "", "");
        var weakItem = new RawItem("fake", "weak", "", "unrelated chatter", "u2", "", "");
        var dupDbItem = new RawItem("fake", "db-dup", "", "hiring outbound SDR", "u3", "", "");
        var dupRunItem1 = new RawItem("fake", "run-dup", "", "hiring outbound SDR", "u4", "", "");
        var dupRunItem2 = new RawItem("fake", "run-dup", "", "hiring outbound SDR", "u4", "", "");
        var goodItem = new RawItem("fake", "good", "", "hiring outbound SDR", "u5", "", "");
        var postEnrichSuppressedItem = new RawItem("fake", "post-supp", "", "hiring outbound SDR", "u6", "", "");
        var source = new FakeSource(suppressedItem, weakItem, dupDbItem, dupRunItem1, dupRunItem2, goodItem, postEnrichSuppressedItem);

        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Is<RawItem>(i => i.ExternalId == "run-dup"), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "s", "subj", "draft", "Widget Co"));
        enricher.EnrichAsync(Arg.Is<RawItem>(i => i.ExternalId == "good"), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "s", "subj", "draft", "Acme"));
        enricher.EnrichAsync(Arg.Is<RawItem>(i => i.ExternalId == "post-supp"), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "s", "subj", "draft", "Bluecore Energy B.V."));

        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        leads.ExistsByFingerprintAsync(dupDbItem.Fingerprint, Arg.Any<CancellationToken>()).Returns(true);

        var customers = Substitute.For<ICustomerRepository>();
        customers.GetByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);

        var suppressions = Substitute.For<ISuppressionRepository>();
        suppressions.ValuesAsync(Arg.Any<CancellationToken>()).Returns(["blockedco", "bluecore energy"]);

        var sut = new HarvestLeads([source], enricher, NoSecondaryContext(), leads, customers, suppressions, Niche(), Substitute.For<ITenantScope>());

        // Act
        var report = await sut.RunAsync(CancellationToken.None);

        // Assert — every distinct reason attributed with the right count. "below-score" no
        // longer exists as a reason — the score gate it named was deleted in Task 3.
        report.Harvested.Should().Be(7);
        report.DropReasons["suppressed"].Should().Be(1);
        report.DropReasons.Should().NotContainKey("below-score");
        report.DropReasons["duplicate-fingerprint"].Should().Be(2);           // DB dup + in-run dup, same reason bucket
        report.DropReasons["suppressed-after-enrichment"].Should().Be(1);

        // Assert — legacy ScoredIn/Deduped positions still mean what they meant structurally
        // before this task, but the cohort they describe grew: ScoredIn now = survived veto and
        // was ranked (6 items: weakItem, dupDbItem, both run-dup items, goodItem,
        // postEnrichSuppressedItem — only suppressedItem never reaches ranking; weakItem is no
        // longer excluded, because there is no score gate left to exclude it).
        // Deduped = fingerprint-dedup drops ONLY (2), not the full Vetoed aggregate — this is
        // exactly the invariant HarvestRunAlert's `ScoredIn - Deduped` arithmetic depends on.
        report.ScoredIn.Should().Be(6);
        report.Deduped.Should().Be(2);
        (report.ScoredIn - report.Deduped).Should().Be(4);   // now the true candidate-pool size (weakItem included)

        // Only goodItem and the first run-dup item survive to be saved; weakItem reaches the
        // (unconfigured) enricher fake and gets a null result — treated as an ordinary LLM-empty
        // outcome, not a drop; the suppressed-after-enrichment company never gets persisted at all.
        report.Saved.Should().Be(2);
        await leads.DidNotReceive().AddAsync(Arg.Is<Lead>(l => l.Company == "Bluecore Energy B.V."), Arg.Any<CancellationToken>());
    }
}

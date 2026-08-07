using Coldstart.Application.Harvest;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Coldstart.Application.Tests;

// The triage stage gates the (far more expensive) enrichment call behind a cheap LLM
// verdict. Reuses HarvestLeadsTests' FakeSource/Niche/NoSecondaryContext/NoSuppressions
// helpers so these tests exercise exactly the same pipeline plumbing as the pre-triage suite.
public class HarvestTriageStageTests
{
    [Fact]
    public async Task RunForCustomerAsync_TriageSaysNotFit_SkipsEnrichmentEntirely()
    {
        // Arrange
        var cid = Guid.NewGuid();
        var customer = Customer.Create("Acme");
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(customer);
        var item = new RawItem("fake", "1", "", "hiring outbound SDR", "", "u1", "");
        var source = new HarvestLeadsTests.FakeSource(item);
        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var enricher = Substitute.For<ILeadEnricher>();
        var triage = Substitute.For<ILeadTriage>();
        triage.JudgeAsync(Arg.Any<RawItem>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TriageVerdict(TriageDecision.NotFit, null, [], ["wrong industry"]));
        var sut = new HarvestLeads([source], enricher, triage, HarvestLeadsTests.NoSecondaryContext(), leads, customers,
            HarvestLeadsTests.NoSuppressions(), HarvestLeadsTests.Niche(), Substitute.For<ITenantScope>());
        var targeting = new HarvestTargeting("offer", ["outbound"], ["hiring"], [], "Acme", "", 0.4, 10);

        // Act
        var report = await sut.RunForCustomerAsync(cid, targeting, CancellationToken.None);

        // Assert — the whole point: no draft is written, no enrichment call is made
        await enricher.DidNotReceive().EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(),
            Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        report.Saved.Should().Be(0);
        report.DropReasons["triage-not-fit"].Should().Be(1);
    }

    [Fact]
    public async Task RunForCustomerAsync_TriageSaysFit_ProceedsToEnrichment()
    {
        // Arrange — the LLM's own COMPANY line came back empty ("-"); the item's raw author
        // field is not the real prospect. The triage verdict's Company must win over it.
        var cid = Guid.NewGuid();
        var customer = Customer.Create("Acme");
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(customer);
        var item = new RawItem("fake", "1", "", "hiring outbound SDR", "someblogger", "u1", "");
        var source = new HarvestLeadsTests.FakeSource(item);
        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "s", "subj", "draft", "-"));
        var triage = Substitute.For<ILeadTriage>();
        triage.JudgeAsync(Arg.Any<RawItem>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TriageVerdict(TriageDecision.Fit, "Acme Real Co", ["outbound"], []));
        var sut = new HarvestLeads([source], enricher, triage, HarvestLeadsTests.NoSecondaryContext(), leads, customers,
            HarvestLeadsTests.NoSuppressions(), HarvestLeadsTests.Niche(), Substitute.For<ITenantScope>());
        var targeting = new HarvestTargeting("offer", ["outbound"], ["hiring"], [], "Acme", "", 0.4, 10);

        // Act
        var report = await sut.RunForCustomerAsync(cid, targeting, CancellationToken.None);

        // Assert — enrichment happened, and the entity-extraction fix: the triage verdict's
        // company wins over item.Author (a publisher would otherwise get stored as the lead)
        report.Saved.Should().Be(1);
        await enricher.Received(1).EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(),
            Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await leads.Received(1).AddAsync(Arg.Is<Lead>(l => l.Company == "Acme Real Co"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunForCustomerAsync_TriageReturnsNull_TreatsCandidateAsUnsureAndSkipsItThisRun()
    {
        // Arrange — one candidate's triage call fails outright (null, not an exception); a
        // second candidate's triage succeeds as Fit, proving the failure is isolated to the
        // one candidate rather than treated as a systemic outage.
        var cid = Guid.NewGuid();
        var customer = Customer.Create("Acme");
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(customer);
        var unsureItem = new RawItem("fake", "unsure", "", "hiring outbound SDR", "", "u1", "");
        var fitItem = new RawItem("fake", "fit", "", "hiring outbound SDR", "", "u2", "");
        var source = new HarvestLeadsTests.FakeSource(unsureItem, fitItem);
        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Is<RawItem>(i => i.ExternalId == "fit"), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "s", "subj", "draft", "Acme"));
        var triage = Substitute.For<ILeadTriage>();
        triage.JudgeAsync(Arg.Is<RawItem>(i => i.ExternalId == "unsure"), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TriageVerdict?)null);
        triage.JudgeAsync(Arg.Is<RawItem>(i => i.ExternalId == "fit"), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TriageVerdict(TriageDecision.Fit, null, [], []));
        var sut = new HarvestLeads([source], enricher, triage, HarvestLeadsTests.NoSecondaryContext(), leads, customers,
            HarvestLeadsTests.NoSuppressions(), HarvestLeadsTests.Niche(), Substitute.For<ITenantScope>());
        var targeting = new HarvestTargeting("offer", ["outbound"], ["hiring"], [], "Acme", "", 0.4, 10);

        // Act
        var report = await sut.RunForCustomerAsync(cid, targeting, CancellationToken.None);

        // Assert — the null-verdict candidate is treated as unsure (not rejected outright);
        // it simply does not proceed this run, while the other candidate is unaffected
        report.DropReasons["triage-unsure"].Should().Be(1);
        await enricher.DidNotReceive().EnrichAsync(Arg.Is<RawItem>(i => i.ExternalId == "unsure"), Arg.Any<NicheOptions>(),
            Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await enricher.Received(1).EnrichAsync(Arg.Is<RawItem>(i => i.ExternalId == "fit"), Arg.Any<NicheOptions>(),
            Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        report.Saved.Should().Be(1);
    }

    [Fact]
    public async Task RunForCustomerAsync_TriageUnavailableEntirely_FallsBackToEnrichingTopCandidates()
    {
        // Arrange — every triage call comes back null this run (e.g. no API key configured).
        // The documented degrade path: no triage must never mean no leads.
        var cid = Guid.NewGuid();
        var customer = Customer.Create("Acme");
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(customer);
        var itemA = new RawItem("fake", "a", "", "hiring outbound SDR", "", "u1", "");
        var itemB = new RawItem("fake", "b", "", "hiring outbound SDR", "", "u2", "");
        var source = new HarvestLeadsTests.FakeSource(itemA, itemB);
        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "s", "subj", "draft", "Acme"));
        var triage = Substitute.For<ILeadTriage>();
        triage.JudgeAsync(Arg.Any<RawItem>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((TriageVerdict?)null);
        var sut = new HarvestLeads([source], enricher, triage, HarvestLeadsTests.NoSecondaryContext(), leads, customers,
            HarvestLeadsTests.NoSuppressions(), HarvestLeadsTests.Niche(), Substitute.For<ITenantScope>());
        var targeting = new HarvestTargeting("offer", ["outbound"], ["hiring"], [], "Acme", "", 0.4, 10);

        // Act
        var report = await sut.RunForCustomerAsync(cid, targeting, CancellationToken.None);

        // Assert — both candidates are enriched directly, bypassing the (unavailable) gate
        await enricher.Received(1).EnrichAsync(Arg.Is<RawItem>(i => i.ExternalId == "a"), Arg.Any<NicheOptions>(),
            Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await enricher.Received(1).EnrichAsync(Arg.Is<RawItem>(i => i.ExternalId == "b"), Arg.Any<NicheOptions>(),
            Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        report.Saved.Should().Be(2);
        // The fallback bypasses per-candidate gating entirely — nothing here was ever
        // attributed to a triage drop reason, or the funnel would misreport genuine
        // enrichments as rejections.
        report.DropReasons.Should().NotContainKey("triage-unsure");
        report.DropReasons.Should().NotContainKey("triage-not-fit");
    }

    [Fact]
    public async Task RunForCustomerAsync_MoreCandidatesThanTheTriageCap_StopsAtTheCap()
    {
        // Arrange — "good" outranks "weak" (it matches the targeting keywords); the triage
        // budget is capped at exactly 1 call, so only the top-ranked candidate is ever judged.
        var cid = Guid.NewGuid();
        var customer = Customer.Create("Acme");
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(customer);
        var good = new RawItem("fake", "good", "", "hiring outbound SDR", "", "u1", "");
        var weak = new RawItem("fake", "weak", "", "unrelated chatter", "", "u2", "");
        var source = new HarvestLeadsTests.FakeSource(good, weak);
        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var enricher = Substitute.For<ILeadEnricher>();
        var triage = Substitute.For<ILeadTriage>();
        triage.JudgeAsync(Arg.Any<RawItem>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TriageVerdict(TriageDecision.Fit, null, [], []));
        var cappedNiche = HarvestLeadsTests.Niche();
        cappedNiche.MaxTriagePerRun = 1;
        var sut = new HarvestLeads([source], enricher, triage, HarvestLeadsTests.NoSecondaryContext(), leads, customers,
            HarvestLeadsTests.NoSuppressions(), cappedNiche, Substitute.For<ITenantScope>());
        var targeting = new HarvestTargeting("offer", ["outbound"], ["hiring"], [], "Acme", "", 0.4, 10);

        // Act
        await sut.RunForCustomerAsync(cid, targeting, CancellationToken.None);

        // Assert — only the top-ranked candidate is judged; the cap stops it there
        await triage.Received(1).JudgeAsync(Arg.Is<RawItem>(i => i.ExternalId == "good"), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await triage.DidNotReceive().JudgeAsync(Arg.Is<RawItem>(i => i.ExternalId == "weak"), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunForCustomerAsync_TriageThrows_DoesNotAbortTheBatch()
    {
        // Arrange — the first candidate's triage call throws outright; the run must continue
        // and still process the next candidate rather than crash the whole batch.
        var cid = Guid.NewGuid();
        var customer = Customer.Create("Acme");
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(customer);
        var throwsItem = new RawItem("fake", "throws", "", "hiring outbound SDR", "", "u1", "");
        var fitItem = new RawItem("fake", "fit", "", "hiring outbound SDR", "", "u2", "");
        var source = new HarvestLeadsTests.FakeSource(throwsItem, fitItem);
        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Is<RawItem>(i => i.ExternalId == "fit"), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "s", "subj", "draft", "Acme"));
        var triage = Substitute.For<ILeadTriage>();
        triage.JudgeAsync(Arg.Is<RawItem>(i => i.ExternalId == "throws"), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));
        triage.JudgeAsync(Arg.Is<RawItem>(i => i.ExternalId == "fit"), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TriageVerdict(TriageDecision.Fit, null, [], []));
        var sut = new HarvestLeads([source], enricher, triage, HarvestLeadsTests.NoSecondaryContext(), leads, customers,
            HarvestLeadsTests.NoSuppressions(), HarvestLeadsTests.Niche(), Substitute.For<ITenantScope>());
        var targeting = new HarvestTargeting("offer", ["outbound"], ["hiring"], [], "Acme", "", 0.4, 10);

        // Act — must not throw
        var report = await sut.RunForCustomerAsync(cid, targeting, CancellationToken.None);

        // Assert — the batch continued past the throwing candidate; the next one still saved
        report.Saved.Should().Be(1);
        await enricher.DidNotReceive().EnrichAsync(Arg.Is<RawItem>(i => i.ExternalId == "throws"), Arg.Any<NicheOptions>(),
            Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await enricher.Received(1).EnrichAsync(Arg.Is<RawItem>(i => i.ExternalId == "fit"), Arg.Any<NicheOptions>(),
            Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}

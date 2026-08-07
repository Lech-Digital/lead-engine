# Lead Quality Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn a single-source funding-news scraper into a five-source lead engine whose ICP filter actually filters, by replacing the quality-as-gate design with veto → prioritise → triage → enrich.

**Architecture:** `HarvestLeads.RunPipelineAsync` currently does harvesting, scoring, gating, dedup, enrichment and persistence in one 60-line method. This plan extracts the pipeline into focused, independently testable units, replaces the drop-threshold with ranking, and inserts a cheap LLM triage stage before expensive enrichment. Instrumentation lands first so every later change is measurable.

**Tech Stack:** .NET 10, EF Core + Postgres (Npgsql), xUnit + NSubstitute + FluentAssertions, Groq (`llama-3.3-70b-versatile`).

**Spec:** `docs/superpowers/specs/2026-08-07-lead-quality-design.md`

## Global Constraints

- CS-HEX-01 Domain has zero outbound dependencies; CS-HEX-02 ports in Application; CS-HEX-03 DI in the composition root only.
- CS-DDD-01 value objects are `record`s; CS-DDD-02 entities protect state with `private set`.
- CS-DATA-03 async all the way; `CancellationToken` threaded to IO. No `.Result`/`.Wait()`.
- CS-MOD-01 prefer `internal` over `public` for implementation classes.
- Tests: xUnit + NSubstitute + FluentAssertions, `MethodName_StateUnderTest_ExpectedBehavior`, strict `// Arrange` / `// Act` / `// Assert`, ≥1 happy + 2 sad paths per new class. Only mock outbound ports; never mock domain entities.
- Conventional commit messages; NEVER a `Co-Authored-By` trailer. Do NOT run `git push`. Do NOT use `git add -A` — stage by path.
- **A failing source must never abort a run.** This holds today and must hold after every task.
- **Budget is enforced as a count of LLM calls, never a computed currency amount.** The €0.002/lead estimate is unverified; a wrong price must not be able to cause overspend.
- Run tests from the repo root: `dotnet test`, or `dotnet test tests/<Project> --filter <Name>`.
- Baseline at plan time: 664 tests green on `main`.

---

## Task 1: Instrumentation — make the funnel visible

Ships first because every later task is judged by what this records. Today `HarvestReport` counts `Harvested/ScoredIn/Deduped/Enriched/Saved/Rejected` with no reason attribution, so "the gate killed four sources" was invisible for months.

**Files:**
- Modify: `src/Coldstart.Application/Harvest/HarvestReport.cs`
- Create: `src/Coldstart.Application/Harvest/HarvestFunnel.cs`
- Modify: `src/Coldstart.Application/Harvest/HarvestLeads.cs`
- Test: `tests/Coldstart.Application.Tests/HarvestFunnelTests.cs` (new)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `HarvestFunnel` (mutable counter object) with `Harvested`, `Vetoed`, `Prioritised`, `Triaged`, `Enriched`, `Saved`, `Rejected` (all `int`), `DropReasons` (`IReadOnlyDictionary<string,int>`), methods `CountHarvested()`, `CountDropped(string reason)`, `CountPrioritised()`, `CountTriaged()`, `CountEnriched()`, `CountSaved()`, `CountRejected()`, `PerSource` (`IReadOnlyDictionary<string,int>` of saved-per-source) and `CountSavedFrom(string source)`. `HarvestReport` gains `IReadOnlyDictionary<string,int> DropReasons` and `IReadOnlyDictionary<string,int> PerSource`, both defaulting to empty so existing constructor calls keep compiling.

- [ ] **Step 1: Write the failing test**

```csharp
using Coldstart.Application.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class HarvestFunnelTests
{
    [Fact]
    public void CountDropped_SameReasonTwice_AggregatesUnderThatReason()
    {
        // Arrange
        var funnel = new HarvestFunnel();

        // Act
        funnel.CountDropped("excluded");
        funnel.CountDropped("excluded");
        funnel.CountDropped("duplicate-company");

        // Assert
        funnel.DropReasons["excluded"].Should().Be(2);
        funnel.DropReasons["duplicate-company"].Should().Be(1);
    }

    [Fact]
    public void CountSavedFrom_MultipleSources_TracksEachSeparately()
    {
        // Arrange
        var funnel = new HarvestFunnel();

        // Act
        funnel.CountSavedFrom("funding");
        funnel.CountSavedFrom("hackernews");
        funnel.CountSavedFrom("funding");

        // Assert — the headline measure: leads must come from more than one source
        funnel.PerSource["funding"].Should().Be(2);
        funnel.PerSource["hackernews"].Should().Be(1);
    }

    [Fact]
    public void NewFunnel_BeforeAnyCounting_IsAllZeroAndEmpty()
    {
        // Act
        var funnel = new HarvestFunnel();

        // Assert
        funnel.Harvested.Should().Be(0);
        funnel.DropReasons.Should().BeEmpty();
        funnel.PerSource.Should().BeEmpty();
    }

    [Fact]
    public void ToReport_AfterCounting_CarriesCountsAndReasonsThrough()
    {
        // Arrange
        var funnel = new HarvestFunnel();
        funnel.CountHarvested();
        funnel.CountDropped("stale");
        funnel.CountSavedFrom("adzuna");

        // Act
        var report = funnel.ToReport(savedLeads: []);

        // Assert
        report.Harvested.Should().Be(1);
        report.DropReasons["stale"].Should().Be(1);
        report.PerSource["adzuna"].Should().Be(1);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Application.Tests --filter "HarvestFunnelTests"`
Expected: FAIL to compile — `HarvestFunnel` does not exist.

- [ ] **Step 3: Implement `HarvestFunnel`**

```csharp
namespace Coldstart.Application.Harvest;

// One run's funnel, counted as it happens. Attribution by reason is the point:
// for months every non-funding source was silently dropped at the score gate and
// the report could not show it, because it counted only totals.
public sealed class HarvestFunnel
{
    private readonly Dictionary<string, int> _dropReasons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _perSource = new(StringComparer.OrdinalIgnoreCase);

    public int Harvested { get; private set; }
    public int Vetoed { get; private set; }
    public int Prioritised { get; private set; }
    public int Triaged { get; private set; }
    public int Enriched { get; private set; }
    public int Saved { get; private set; }
    public int Rejected { get; private set; }

    public IReadOnlyDictionary<string, int> DropReasons => _dropReasons;
    public IReadOnlyDictionary<string, int> PerSource => _perSource;

    public void CountHarvested() => Harvested++;
    public void CountPrioritised() => Prioritised++;
    public void CountTriaged() => Triaged++;
    public void CountEnriched() => Enriched++;
    public void CountRejected() => Rejected++;

    public void CountDropped(string reason)
    {
        Vetoed++;
        _dropReasons[reason] = _dropReasons.TryGetValue(reason, out var n) ? n + 1 : 1;
    }

    public void CountSavedFrom(string source)
    {
        Saved++;
        _perSource[source] = _perSource.TryGetValue(source, out var n) ? n + 1 : 1;
    }

    public HarvestReport ToReport(IReadOnlyList<SavedLead> savedLeads) =>
        new(Harvested, Prioritised, Vetoed, Enriched, Saved, Rejected, savedLeads, _dropReasons, _perSource);
}
```

Extend `HarvestReport` with the two new optional members, keeping every existing call site compiling:

```csharp
public sealed record HarvestReport(int Harvested, int ScoredIn, int Deduped, int Enriched, int Saved, int Rejected,
    IReadOnlyList<SavedLead>? SavedLeads = null,
    IReadOnlyDictionary<string, int>? DropReasons = null,
    IReadOnlyDictionary<string, int>? PerSource = null)
{
    public IReadOnlyList<SavedLead> NewLeads => SavedLeads ?? [];
    public IReadOnlyDictionary<string, int> DroppedBy => DropReasons ?? new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> SavedBySource => PerSource ?? new Dictionary<string, int>();
}
```

- [ ] **Step 4: Wire the funnel into the pipeline**

In `HarvestLeads.RunPipelineAsync`, replace the local `int harvested, scoredIn, …` counters with a single `var funnel = new HarvestFunnel();`, call the matching `Count…` methods at each existing decision point, and return `funnel.ToReport(savedLeads)`. Give every existing `continue` a reason string: `"suppressed"` for the GDPR objection list, `"below-score"` for the score gate, `"duplicate-fingerprint"` for both dedup checks. Do not change any behaviour in this task — only what is recorded.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: ALL PASS. No pre-existing harvest test should need changing; if one does, behaviour moved and this task did too much.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Application/Harvest/HarvestFunnel.cs src/Coldstart.Application/Harvest/HarvestReport.cs src/Coldstart.Application/Harvest/HarvestLeads.cs tests/Coldstart.Application.Tests/HarvestFunnelTests.cs
git commit -m "feat(harvest): funnel instrumentation with per-stage drop reasons and per-source attribution"
```

---

## Task 2: Veto — exclusions, denylist, company dedup, freshness

The first quality win, and it needs no LLM. Today exclusions only reach the LLM, so enrichment is paid for on companies already known to be wrong; publishers and VC firms are recorded as prospects; and 55% of leads are duplicate companies.

**Files:**
- Create: `src/Coldstart.Application/Harvest/CandidateVeto.cs`
- Create: `src/Coldstart.Application/Harvest/PublisherDenylist.cs`
- Modify: `src/Coldstart.Application/Harvest/HarvestLeads.cs`
- Test: `tests/Coldstart.Application.Tests/CandidateVetoTests.cs` (new)

**Interfaces:**
- Consumes: `HarvestFunnel.CountDropped(string)` (Task 1); `RawItem(Source, ExternalId, Title, Text, Author, Url, CreatedAt)` with computed `Fingerprint`; `HarvestTargeting.Disqualifiers` (`IReadOnlyList<string>`).
- Produces:
  - `PublisherDenylist.IsPublisherOrInvestor(string? name)` → `bool`.
  - `CandidateVeto.Check(RawItem item, IReadOnlyList<string> exclusions, ISet<string> alreadyHeldCompanies, DateTimeOffset now, TimeSpan freshness)` → `string?` — the drop reason, or `null` to keep. Reasons: `"excluded"`, `"publisher-or-investor"`, `"duplicate-company"`, `"stale"`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Coldstart.Application.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class CandidateVetoTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Freshness = TimeSpan.FromDays(14);

    private static RawItem Item(string title, string author = "Acme BV", string text = "") =>
        new("funding", "id-" + title, title, text, author, "https://example.com/x", Now.ToString("O"));

    [Fact]
    public void Check_ItemMatchingAnExclusion_IsVetoedBeforeAnySpend()
    {
        // Arrange — the customer excludes recruiters
        var item = Item("Recruiters raise a round", text: "a recruitment agency");

        // Act
        var reason = CandidateVeto.Check(item, ["recruitment agency"], new HashSet<string>(), Now, Freshness);

        // Assert
        reason.Should().Be("excluded");
    }

    [Fact]
    public void Check_PublisherAsTheCompany_IsVetoed()
    {
        // Arrange — real observed junk: the feed's own publisher recorded as the prospect
        var item = Item("Some startup raises €10M", author: "news.crunchbase.com");

        // Act
        var reason = CandidateVeto.Check(item, [], new HashSet<string>(), Now, Freshness);

        // Assert
        reason.Should().Be("publisher-or-investor");
    }

    [Fact]
    public void Check_InvestorAsTheCompany_IsVetoed()
    {
        // Arrange — real observed junk: the VC, not the funded company
        var item = Item("Round led by the fund", author: "Lightspeed Venture Partners");

        // Act
        var reason = CandidateVeto.Check(item, [], new HashSet<string>(), Now, Freshness);

        // Assert
        reason.Should().Be("publisher-or-investor");
    }

    [Fact]
    public void Check_CompanyAlreadyHeldForThisCustomer_IsVetoedCaseInsensitively()
    {
        // Arrange — "Iceye" and "ICEYE" both appeared as separate leads in production
        var item = Item("ICEYE raises again", author: "ICEYE");
        var held = new HashSet<string>(["iceye"], StringComparer.OrdinalIgnoreCase);

        // Act
        var reason = CandidateVeto.Check(item, [], held, Now, Freshness);

        // Assert
        reason.Should().Be("duplicate-company");
    }

    [Fact]
    public void Check_ItemOlderThanTheFreshnessWindow_IsVetoed()
    {
        // Arrange
        var stale = new RawItem("funding", "id-old", "Old news", "", "Acme BV",
            "https://example.com/x", Now.AddDays(-30).ToString("O"));

        // Act
        var reason = CandidateVeto.Check(stale, [], new HashSet<string>(), Now, Freshness);

        // Assert — congratulating someone on month-old news is the failure this prevents
        reason.Should().Be("stale");
    }

    [Fact]
    public void Check_UnparseableDate_IsKeptRatherThanSilentlyDropped()
    {
        // Arrange — several sources emit no usable date at all
        var item = new RawItem("hackernews", "id-1", "Founder needs customers", "", "pg",
            "https://example.com/x", "not-a-date");

        // Act
        var reason = CandidateVeto.Check(item, [], new HashSet<string>(), Now, Freshness);

        // Assert — an absent date must not be treated as stale, or whole sources vanish
        reason.Should().BeNull();
    }

    [Fact]
    public void Check_GenuineProspect_IsKept()
    {
        // Arrange
        var item = Item("Acme BV raises seed round", text: "B2B software for logistics");

        // Act
        var reason = CandidateVeto.Check(item, ["recruitment agency"], new HashSet<string>(), Now, Freshness);

        // Assert
        reason.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Coldstart.Application.Tests --filter "CandidateVetoTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Implement the denylist**

```csharp
namespace Coldstart.Application.Harvest;

// Feeds routinely name the publisher or the investor rather than the funded company.
// Production showed tech.eu, news.crunchbase.com, Lightspeed Venture Partners, Menlo
// Ventures and White Star Capital all stored as prospects. Matching is substring-based
// on a normalised name so "Lightspeed Venture Partners" and "Lightspeed" both hit.
internal static class PublisherDenylist
{
    private static readonly string[] Publishers =
    [
        "techcrunch", "tech.eu", "crunchbase", "sifted", "eu-startups", "silicon canals",
        "venturebeat", "the next web", "businesswire", "pr newswire", "globenewswire",
    ];

    private static readonly string[] InvestorMarkers =
    [
        "ventures", "venture partners", "capital", "partners llp", "vc fund",
        "equity", "invest", "fund i", "fund ii", "seed fund",
    ];

    public static bool IsPublisherOrInvestor(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim().ToLowerInvariant();
        return Publishers.Any(p => n.Contains(p, StringComparison.Ordinal))
            || InvestorMarkers.Any(m => n.Contains(m, StringComparison.Ordinal));
    }
}
```

- [ ] **Step 4: Implement the veto**

```csharp
namespace Coldstart.Application.Harvest;

// Stage 1 of the pipeline: drop only what is provably wrong, before any spend.
// Deliberately contains NO keyword scoring — using a quality heuristic as a cost
// control is what reduced this engine to a single source.
internal static class CandidateVeto
{
    public static string? Check(
        RawItem item,
        IReadOnlyList<string> exclusions,
        ISet<string> alreadyHeldCompanies,
        DateTimeOffset now,
        TimeSpan freshness)
    {
        if (PublisherDenylist.IsPublisherOrInvestor(item.Author)) return "publisher-or-investor";

        var haystack = $"{item.Title}\n{item.Text}\n{item.Author}".ToLowerInvariant();
        if (exclusions.Any(e => !string.IsNullOrWhiteSpace(e) && haystack.Contains(e.Trim().ToLowerInvariant(), StringComparison.Ordinal)))
            return "excluded";

        if (!string.IsNullOrWhiteSpace(item.Author) && alreadyHeldCompanies.Contains(item.Author.Trim()))
            return "duplicate-company";

        // An unparseable or absent date must never count as stale: several sources emit
        // no usable date, and treating that as old would silently delete whole sources.
        if (DateTimeOffset.TryParse(item.CreatedAt, out var created) && created < now - freshness)
            return "stale";

        return null;
    }
}
```

- [ ] **Step 5: Wire it into the pipeline**

In `HarvestLeads.RunPipelineAsync`, immediately after the existing suppression check, build the already-held company set once per run from `leads.ForCustomerAsync(cid, ct)` (case-insensitive on `Lead.Company`), then call `CandidateVeto.Check(...)`. On a non-null reason call `funnel.CountDropped(reason)` and `continue`. Pass `targeting.Disqualifiers` as the exclusions and `TimeSpan.FromDays(14)` as the freshness window. Leave the existing score gate in place for now — Task 3 removes it.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: ALL PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Coldstart.Application/Harvest/CandidateVeto.cs src/Coldstart.Application/Harvest/PublisherDenylist.cs src/Coldstart.Application/Harvest/HarvestLeads.cs tests/Coldstart.Application.Tests/CandidateVetoTests.cs
git commit -m "feat(harvest): veto stage — exclusions, publisher/investor denylist, company dedup, freshness"
```

---

## Task 3: Prioritise — rank instead of gate

The change that revives four dead sources. `MinScore` stops being a drop-threshold; candidates are ordered and the best ones get the spend.

**Files:**
- Create: `src/Coldstart.Application/Harvest/CandidatePriority.cs`
- Modify: `src/Coldstart.Application/Harvest/HarvestLeads.cs`
- Test: `tests/Coldstart.Application.Tests/CandidatePriorityTests.cs` (new)

**Interfaces:**
- Consumes: `SignalScorer.Score(RawItem, NicheOptions, double baseScore, double scoreWeight)` → `SignalResult(double Score, IReadOnlyList<string> Matched)`.
- Produces: `CandidatePriority.Rank(SignalResult signal, double sourceBaseScore, DateTimeOffset? createdAt, DateTimeOffset now)` → `double`. Higher is better. Never drops anything.

- [ ] **Step 1: Write the failing tests**

```csharp
using Coldstart.Application.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class CandidatePriorityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Rank_ItemWithMatchedSignals_OutranksOneWithout()
    {
        // Arrange
        var matched = new SignalResult(0.8, ["raised seed"]);
        var unmatched = new SignalResult(0.25, []);

        // Act
        var withSignal = CandidatePriority.Rank(matched, 0.25, Now, Now);
        var without = CandidatePriority.Rank(unmatched, 0.25, Now, Now);

        // Assert
        withSignal.Should().BeGreaterThan(without);
    }

    [Fact]
    public void Rank_LowBaseScoreSource_StillProducesAUsableRank()
    {
        // Arrange — LangSearch's 0.25 base is below the old 0.4 gate, which killed it entirely
        var signal = new SignalResult(0.25, []);

        // Act
        var rank = CandidatePriority.Rank(signal, 0.25, Now, Now);

        // Assert — it must compete, not be discarded
        rank.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Rank_FresherItem_OutranksAnOlderOneWithIdenticalSignal()
    {
        // Arrange
        var signal = new SignalResult(0.5, []);

        // Act
        var fresh = CandidatePriority.Rank(signal, 0.5, Now, Now);
        var old = CandidatePriority.Rank(signal, 0.5, Now.AddDays(-10), Now);

        // Assert
        fresh.Should().BeGreaterThan(old);
    }

    [Fact]
    public void Rank_UnknownDate_IsRankedWithoutARecencyBonusRatherThanPenalised()
    {
        // Arrange
        var signal = new SignalResult(0.5, []);

        // Act
        var unknown = CandidatePriority.Rank(signal, 0.5, null, Now);
        var old = CandidatePriority.Rank(signal, 0.5, Now.AddDays(-10), Now);

        // Assert — a missing date must not push a source to the bottom forever
        unknown.Should().BeGreaterThanOrEqualTo(old);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Coldstart.Application.Tests --filter "CandidatePriorityTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Implement**

```csharp
namespace Coldstart.Application.Harvest;

// Stage 2: ordering, not gating. The old design dropped anything below MinScore, and
// because keyword matching never fired, only sources whose BASE score already cleared
// the bar survived — funding at 0.50 lived, web search 0.35 / Adzuna 0.35 /
// LangSearch 0.25 / Hacker News all died. Ranking lets every source compete for the
// same spend without an absolute bar.
internal static class CandidatePriority
{
    private const double SignalWeight = 0.6;
    private const double SourceWeight = 0.3;
    private const double RecencyWeight = 0.1;
    private static readonly TimeSpan RecencyHorizon = TimeSpan.FromDays(14);

    public static double Rank(SignalResult signal, double sourceBaseScore, DateTimeOffset? createdAt, DateTimeOffset now)
    {
        var signalPart = signal.Matched.Count > 0 ? signal.Score : 0;
        // No date is not evidence of staleness — give the neutral mid value rather than
        // zero, so a source that never emits dates isn't permanently last.
        var recencyPart = createdAt is { } created
            ? Math.Clamp(1 - (now - created).TotalDays / RecencyHorizon.TotalDays, 0, 1)
            : 0.5;

        return Math.Round(SignalWeight * signalPart + SourceWeight * sourceBaseScore + RecencyWeight * recencyPart, 4);
    }
}
```

- [ ] **Step 4: Replace the gate with ranking in the pipeline**

In `HarvestLeads.RunPipelineAsync`:

1. Delete the line `if (signal.Score < runNiche.MinScore) continue;` — this is the change that revives the dead sources.
2. Keep calling `SignalScorer.Score(...)` (its `Matched` list is still shown to the customer as "why now" and still feeds the rank).
3. Compute `var rank = CandidatePriority.Rank(signal, source.BaseScore, ParsedDate(item), DateTimeOffset.UtcNow);` and carry it alongside the candidate.
4. Order candidates by `rank` descending instead of by `signal.Score`.
5. Call `funnel.CountPrioritised()` for each candidate that enters the ordered list.

Add a small private helper in the same file for parsing `item.CreatedAt` into `DateTimeOffset?`, returning null when unparseable.

- [ ] **Step 5: Run the full suite and read the diff in the numbers**

Run: `dotnet test`
Expected: ALL PASS. If a pre-existing harvest test asserted that low-scoring items are dropped, that test encoded the bug — update it to assert the item is *ranked lower* rather than discarded, and name it in your report.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Application/Harvest/CandidatePriority.cs src/Coldstart.Application/Harvest/HarvestLeads.cs tests/Coldstart.Application.Tests/CandidatePriorityTests.cs
git commit -m "feat(harvest): rank candidates instead of gating them, reviving four dead sources"
```

---

## Task 4: Triage — cheap verdicts at volume

**Files:**
- Create: `src/Coldstart.Application/Harvest/ILeadTriage.cs`
- Create: `src/Coldstart.Application/Harvest/TriageVerdictParser.cs`
- Create: `src/Coldstart.Infrastructure/Harvest/GroqLeadTriage.cs`
- Modify: `src/Coldstart.Infrastructure/DependencyInjection.cs`
- Test: `tests/Coldstart.Application.Tests/TriageVerdictParserTests.cs`, `tests/Coldstart.Infrastructure.Tests/GroqLeadTriageTests.cs` (both new)

**Interfaces:**
- Consumes: `Targeting` (industries, geographies, buyer roles, size band, triggers, exclusions) via a formatted criteria string; `RawItem`.
- Produces:
  - `TriageVerdict(TriageDecision Decision, string? Company, IReadOnlyList<string> Met, IReadOnlyList<string> Failed)`.
  - `enum TriageDecision { Fit, NotFit, Unsure }`.
  - `ILeadTriage.JudgeAsync(RawItem item, string criteria, CancellationToken ct)` → `TriageVerdict?` (null on any failure).
  - `TriageVerdictParser.Parse(string content)` → `TriageVerdict?`.

- [ ] **Step 1: Write the failing parser tests**

```csharp
using Coldstart.Application.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class TriageVerdictParserTests
{
    [Fact]
    public void Parse_FitVerdict_ReturnsCompanyAndCriteria()
    {
        // Arrange
        var content = """
            VERDICT: FIT
            COMPANY: Acme BV
            MET: early stage, Netherlands
            FAILED: none
            """;

        // Act
        var verdict = TriageVerdictParser.Parse(content);

        // Assert
        verdict.Should().NotBeNull();
        verdict!.Decision.Should().Be(TriageDecision.Fit);
        verdict.Company.Should().Be("Acme BV");
        verdict.Met.Should().Contain("early stage");
    }

    [Fact]
    public void Parse_NotFitVerdict_CarriesTheFailedCriteria()
    {
        // Arrange
        var content = "VERDICT: NOT FIT\nCOMPANY: Menlo Ventures\nMET: none\nFAILED: is an investor, not a prospect";

        // Act
        var verdict = TriageVerdictParser.Parse(content);

        // Assert
        verdict.Should().NotBeNull();
        verdict!.Decision.Should().Be(TriageDecision.NotFit);
        verdict.Failed.Should().Contain(f => f.Contains("investor"));
    }

    [Fact]
    public void Parse_UnsureVerdict_IsDistinctFromNotFit()
    {
        // Act
        var verdict = TriageVerdictParser.Parse("VERDICT: UNSURE\nCOMPANY: Acme");

        // Assert — unsure must not be silently treated as a rejection
        verdict!.Decision.Should().Be(TriageDecision.Unsure);
    }

    [Fact]
    public void Parse_NoRecognisableVerdict_ReturnsNull()
    {
        // Act / Assert — an unparseable answer is a failure, never a silent rejection
        TriageVerdictParser.Parse("I'm sorry, I can't help with that.").Should().BeNull();
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsNull()
    {
        // Act / Assert
        TriageVerdictParser.Parse("").Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Coldstart.Application.Tests --filter "TriageVerdictParserTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Implement the port, verdict and parser**

Create `ILeadTriage.cs` holding `TriageDecision`, `TriageVerdict` and `ILeadTriage`. Create `TriageVerdictParser` modelled closely on the existing `src/Coldstart.Application/Customers/TargetingProposalParser.cs` — read that file first and mirror its label-slicing approach, with labels `VERDICT`, `COMPANY`, `MET`, `FAILED`. `VERDICT` is required; if it is absent or unrecognised, return null. Map `FIT` → `TriageDecision.Fit`, `NOT FIT`/`NOTFIT`/`NO` → `NotFit`, `UNSURE`/`MAYBE` → `Unsure`. Treat a `MET`/`FAILED` value of `none` as an empty list.

- [ ] **Step 4: Implement the Groq triage client**

Create `GroqLeadTriage` in Infrastructure, modelled on `src/Coldstart.Infrastructure/Harvest/GroqLeadEnricher.cs` — read it first and mirror its options usage, retry/backoff, request pacing and null-on-failure contract exactly. Differences: a much shorter prompt, and `TriageVerdictParser.Parse` instead of `EnrichmentParser.Parse`.

The prompt must: state the customer's ICP criteria; give the harvested item; ask for exactly the four labels; instruct the model to answer `UNSURE` rather than guess when the content is too thin; and require the real company name — explicitly *not* the publisher or the investor, naming that failure mode, since that is exactly what production got wrong. Keep the output tiny: this stage is cheap only because it does not write prose.

Register it in `src/Coldstart.Infrastructure/DependencyInjection.cs` beside `ILeadEnricher`.

- [ ] **Step 5: Write the client tests**

`tests/Coldstart.Infrastructure.Tests/GroqLeadTriageTests.cs`, reusing the fake `HttpMessageHandler` fixture from `GroqLeadEnricherTests.cs`. Cover: a well-formed response yields the verdict; a non-success status yields null; a success response with unparseable content yields null; a missing API key yields null with no HTTP call.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Coldstart.Application.Tests` then `dotnet test tests/Coldstart.Infrastructure.Tests`
Expected: ALL PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Coldstart.Application/Harvest/ILeadTriage.cs src/Coldstart.Application/Harvest/TriageVerdictParser.cs src/Coldstart.Infrastructure/Harvest/GroqLeadTriage.cs src/Coldstart.Infrastructure/DependencyInjection.cs tests/
git commit -m "feat(harvest): cheap LLM triage verdict with explicit ICP criteria"
```

---

## Task 5: Wire triage into the pipeline, with budget caps

**Files:**
- Modify: `src/Coldstart.Application/Harvest/HarvestLeads.cs`
- Modify: `src/Coldstart.Application/Harvest/NicheOptions.cs`
- Create: `src/Coldstart.Application/Harvest/TriageCriteria.cs`
- Test: `tests/Coldstart.Application.Tests/HarvestTriageStageTests.cs` (new)

**Interfaces:**
- Consumes: `ILeadTriage.JudgeAsync` (Task 4), `HarvestFunnel` (Task 1), `CandidatePriority.Rank` (Task 3), `Targeting`.
- Produces: `TriageCriteria.Describe(Targeting targeting, string offer)` → `string`; `NicheOptions.MaxTriagePerRun` (`int`, default 200).

- [ ] **Step 1: Write the failing tests**

`tests/Coldstart.Application.Tests/HarvestTriageStageTests.cs` — substitute `ILeadTriage` and `ILeadEnricher` and assert, using the existing `HarvestLeadsTests` fixture style (read that file first and reuse its source/repository substitutes):

```csharp
[Fact]
public async Task RunForCustomerAsync_TriageSaysNotFit_SkipsEnrichmentEntirely()
{
    // Assert — the whole point: no draft is written, no enrichment call is made
    await enricher.DidNotReceive().EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(),
        Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task RunForCustomerAsync_TriageSaysFit_ProceedsToEnrichment() { }

[Fact]
public async Task RunForCustomerAsync_TriageReturnsNull_TreatsCandidateAsUnsureAndSkipsItThisRun() { }

[Fact]
public async Task RunForCustomerAsync_TriageUnavailableEntirely_FallsBackToEnrichingTopCandidates()
{
    // The documented degrade path: no triage must never mean no leads
}

[Fact]
public async Task RunForCustomerAsync_MoreCandidatesThanTheTriageCap_StopsAtTheCap() { }

[Fact]
public async Task RunForCustomerAsync_TriageThrows_DoesNotAbortTheBatch() { }
```

Fill in every body using the fixture's helpers — do not leave any as an empty stub.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Coldstart.Application.Tests --filter "HarvestTriageStageTests"`
Expected: FAIL.

- [ ] **Step 3: Implement `TriageCriteria`**

A pure formatter turning a `Targeting` plus the offer into the criteria block the triage prompt shows the model: one line per dimension, omitting empty ones, and stating the size band as a range only when set. Keep it deterministic and unit-testable.

- [ ] **Step 4: Insert the triage stage**

In `HarvestLeads.RunPipelineAsync`, between prioritisation and enrichment:

- Take the top `MaxTriagePerRun` ranked candidates.
- For each, call `triage.JudgeAsync(item, criteria, ct)` inside a try/catch that treats any failure as `Unsure` and continues to the next candidate — a failing triage must never abort the batch.
- `Fit` proceeds to enrichment (up to `MaxEnrichPerRun`); `NotFit` and `Unsure` do not, and are counted via `funnel.CountDropped("triage-not-fit")` / `"triage-unsure"`.
- When the verdict carries a `Company`, prefer it over `item.Author` as the lead's company — this is the entity-extraction fix.
- Call `funnel.CountTriaged()` per judged candidate.
- If `ILeadTriage` is unavailable (the implementation returns null for every call because no API key is configured), fall back to today's behaviour: enrich the top `MaxEnrichPerRun` ranked candidates directly. Detect this by counting consecutive nulls rather than by inspecting configuration, so the Application layer stays free of provider knowledge.

Add `MaxTriagePerRun` to `NicheOptions` with a default of 200 and a comment recording that the budget is a call count, not a currency amount, because the per-token price estimate is unverified.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: ALL PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Application/Harvest/ tests/Coldstart.Application.Tests/HarvestTriageStageTests.cs
git commit -m "feat(harvest): triage stage gates enrichment, with a call-count budget cap"
```

---

## Task 6: Split the pipeline and verify

`RunPipelineAsync` now carries veto, ranking, triage, enrichment and persistence. It was already too long before this work; it must not be left worse.

**Files:**
- Create: `src/Coldstart.Application/Harvest/CandidateCollector.cs`
- Modify: `src/Coldstart.Application/Harvest/HarvestLeads.cs`
- Test: existing suites must pass unchanged

**Interfaces:**
- Produces: `CandidateCollector.CollectAsync(...)` returning the vetoed, ranked candidate list, so `HarvestLeads` is left orchestrating triage → enrichment → persistence only.

- [ ] **Step 1: Extract, do not rewrite**

Move the fetch/veto/rank loop into `CandidateCollector` verbatim. Behaviour must not change; this is a structural extraction. If a test fails, the extraction changed behaviour — revert and redo it.

- [ ] **Step 2: Run the full suite**

Run: `dotnet test`
Expected: ALL PASS with no test edits.

- [ ] **Step 3: Commit**

```bash
git add src/Coldstart.Application/Harvest/
git commit -m "refactor(harvest): extract candidate collection from the pipeline orchestrator"
```

- [ ] **Step 4: Full verification**

Run: `dotnet build && dotnet test`
Expected: build clean, ALL PASS. Note `-warnaserror` fails on three pre-existing NU1510 package warnings unrelated to this work — use the plain build.

- [ ] **Step 5: Report the funnel a dry run produces**

Run a harvest against a seeded test customer and report the resulting `HarvestReport.DroppedBy` and `SavedBySource` maps. These are the numbers the spec's measurement section will be judged against.

## Self-Review Notes

- **Spec coverage:** instrumentation (T1), veto with exclusions/denylist/company-dedup/freshness (T2), prioritise-not-gate (T3), triage with ICP criteria and entity fix (T4, T5), budget as call counts (T5), degrade path when triage is unavailable (T5), pipeline split (T6), measurement (T1 + T6 step 5). Error handling: failing source never aborts (preserved throughout), per-candidate triage failure isolated (T5), budget exhaustion is a normal outcome (T5).
- **Type consistency:** `HarvestFunnel.CountDropped(string)` (T1) is called with the reason strings produced by `CandidateVeto.Check` (T2) and the triage stage (T5). `CandidatePriority.Rank(signal, sourceBaseScore, createdAt, now)` (T3) matches its call site. `TriageVerdict`/`TriageDecision`/`ILeadTriage.JudgeAsync` (T4) match their use in T5.
- **Deliberate deferral:** ICP-aware *drafting* (feeding triage reasons into the enrichment prompt) is listed as step 4 of the spec's sequencing but is NOT in this plan. Triage already gates enrichment, which is where the quality gain is; threading the reasons into the draft is a separate, smaller change best made once real triage output can be inspected. Flagged so it is not mistaken for an omission.
- **Known judgment calls:** the exact publisher/investor lists in T2 (start with the observed offenders, expect to extend), the rank weights in T3 (0.6/0.3/0.1 is a starting point, tunable once the funnel is visible), and the 14-day freshness window in T2.

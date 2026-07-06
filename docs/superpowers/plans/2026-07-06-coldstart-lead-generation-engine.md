# Coldstart .NET Lead-Generation Engine — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the Python engine's lead-generation core to .NET — harvest (HackerNews + funding RSS) → deterministic score gate → dedup → Groq LLM qualify+draft → save real filtered leads under a house customer, runnable on demand.

**Architecture:** Hexagonal over the existing layers. An Application use case `IHarvestLeads` orchestrates ports `ILeadSource` (×N) and `ILeadEnricher`; Infrastructure supplies the HTTP/XML source adapters + a Groq enricher; a small console entry runs it. Pure logic (`SignalScorer`, fingerprint dedup) lives in Application.

**Tech Stack:** .NET 10, `HttpClient` + `System.Text.Json` (HN + Groq), `System.Xml.Linq` (RSS/Atom), EF Core 10 + Npgsql (Postgres 17), xUnit + NSubstitute + FluentAssertions 7.*, Testcontainers.PostgreSql.

## Global Constraints

- **TFM** `net10.0`; build **0 warnings**.
- **Hexagonal:** ports in Application; adapters + DI extensions in Infrastructure/composition root; Domain zero external deps.
- **DDD:** value objects `record`; entities via factories + `private set`; `DomainException` on invalid.
- **CS-DATA:** repositories return `Task<IReadOnlyList<T>>` / `Task`, no `IQueryable` leak; async + `CancellationToken` on all IO.
- **CS-MOD:** implementation classes `internal`; `[InternalsVisibleTo]` test-only (already present in Application & Infrastructure).
- **EF Core pinned 10.0.4**; Testcontainers ctor `new PostgreSqlBuilder("postgres:17")`.
- **LLM = Groq** (`llama-3.3-70b-versatile`), OpenAI-compatible HTTP API; **no Anthropic/Claude dependency**. API key from config/user-secrets (`Llm:ApiKey`), never committed.
- **Testing:** strict three-phase AAA; unit tests for pure logic + parsers with fakes; Testcontainers for the end-to-end pipeline with fake sources + fake enricher; network/live calls are trait-gated, not in the default suite.
- **Builds on** the current `dotnet-port` branch (SP1 + Frontend/Customer-Zone). Repo-root `NuGet.Config` required. Docker running for integration tests.
- **Port source of truth** (translate faithfully): `leadengine/signals.py`, `leadengine/sources/hackernews.py`, `leadengine/sources/funding.py`, `leadengine/enrich.py`, `leadengine/pipeline.py::run`, `clients/coldstart.yaml`.

---

## File Structure

```
src/Coldstart.Application/Harvest/
  RawItem.cs            NicheOptions.cs   SignalResult.cs   SignalScorer.cs
  ILeadSource.cs        ILeadEnricher.cs  EnrichmentResult.cs
  IHarvestLeads.cs      HarvestLeads.cs   HarvestReport.cs
  DependencyInjection.cs (add HarvestLeads registration)
src/Coldstart.Application/Ports/ILeadRepository.cs   (add ExistsByFingerprintAsync)
src/Coldstart.Domain/Lead.cs                          (add provenance + CreateHarvested)
src/Coldstart.Infrastructure/
  Harvest/HackerNewsSource.cs   Harvest/FundingRssSource.cs   Harvest/GroqLeadEnricher.cs
  Harvest/LlmOptions.cs
  Configurations/LeadConfiguration.cs (map provenance + unique index)
  Repositories/LeadRepository.cs (implement ExistsByFingerprintAsync)
  DependencyInjection.cs (register sources + enricher + HttpClient)
  Migrations/ (AddLeadProvenance)
src/Coldstart.Harvester/            (new console project: builds host, runs IHarvestLeads)
  Program.cs   Coldstart.Harvester.csproj   appsettings.json
tests/Coldstart.Application.Tests/   SignalScorerTests / EnrichmentParsingTests / HarvestLeadsTests
tests/Coldstart.Infrastructure.Tests/ HackerNewsSourceTests / FundingRssSourceTests / LeadRepositoryTests(+dedup)
```

---

### Task 1: Niche config, RawItem, and the deterministic SignalScorer

**Files:**
- Create: `src/Coldstart.Application/Harvest/RawItem.cs`, `NicheOptions.cs`, `SignalResult.cs`, `SignalScorer.cs`
- Test: `tests/Coldstart.Application.Tests/SignalScorerTests.cs`

**Interfaces:**
- Produces:
  - `record RawItem(string Source, string ExternalId, string Title, string Text, string Author, string Url, string CreatedAt) { string Fingerprint { get; } }` — SHA-256 hex of `"{Source}:{ExternalId}"`.
  - `sealed class NicheOptions { string Offer; string SenderName; string SenderTitle; List<string> TargetKeywords; List<string> BuyingSignals; List<string> Disqualifiers; double MinScore; int MaxEnrichPerRun; HackerNewsOptions HackerNews; FundingOptions Funding; }` with nested `HackerNewsOptions { List<string> Queries; int MaxPerQuery=30; int LookbackHours=336; double ScoreWeight=0.7; }` and `FundingOptions { List<string> Feeds; int LookbackDays=45; int MaxPerFeed=40; bool RequireFundingTerms=true; double BaseScore=0.5; }`.
  - `record SignalResult(double Score, IReadOnlyList<string> Matched)`.
  - `static class SignalScorer { SignalResult Score(RawItem item, NicheOptions niche, double baseScore, double scoreWeight) }`.

- [ ] **Step 1: Write the failing scorer test** (parity with `signals.py`)

`tests/Coldstart.Application.Tests/SignalScorerTests.cs`:

```csharp
using Coldstart.Application.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class SignalScorerTests
{
    private static NicheOptions Niche() => new()
    {
        TargetKeywords = ["SDR", "outbound", "pipeline"],
        BuyingSignals = ["hiring", "raised", "series a"],
    };

    private static RawItem Item(string text) => new("t", "1", "", text, "", "", "");

    [Fact]
    public void Score_NoMatches_IsZero()
    {
        // Arrange
        var niche = Niche();

        // Act
        var r = SignalScorer.Score(Item("nothing relevant here"), niche, baseScore: 0, scoreWeight: 1);

        // Assert
        r.Score.Should().Be(0);
        r.Matched.Should().BeEmpty();
    }

    [Fact]
    public void Score_OneTargetOneSignal_IsPointSix()
    {
        // Arrange — 1 target hit (0.6) + 1 signal hit (0.6): 0.5*0.6 + 0.5*0.6 = 0.6
        var niche = Niche();

        // Act
        var r = SignalScorer.Score(Item("we are hiring an outbound SDR"), niche, baseScore: 0, scoreWeight: 1);

        // Assert
        r.Score.Should().BeApproximately(0.6, 0.001);
        r.Matched.Should().Contain("outbound").And.Contain("hiring");
    }

    [Fact]
    public void Score_BaseScoreFloor_RaisesButNeverLowers()
    {
        // Arrange — no keyword hits, but a funding-style base floor
        var niche = Niche();

        // Act
        var r = SignalScorer.Score(Item("Acme announced a new round"), niche, baseScore: 0.5, scoreWeight: 1);

        // Assert
        r.Score.Should().Be(0.5);
    }

    [Fact]
    public void Score_Weight_DemotesKeywordScore()
    {
        // Arrange
        var niche = Niche();

        // Act
        var full = SignalScorer.Score(Item("hiring outbound SDR pipeline"), niche, 0, 1.0);
        var demoted = SignalScorer.Score(Item("hiring outbound SDR pipeline"), niche, 0, 0.7);

        // Assert
        demoted.Score.Should().BeLessThan(full.Score);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Application.Tests --filter SignalScorerTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement RawItem, NicheOptions, SignalResult, SignalScorer**

`src/Coldstart.Application/Harvest/RawItem.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Coldstart.Application.Harvest;

public sealed record RawItem(string Source, string ExternalId, string Title, string Text,
    string Author, string Url, string CreatedAt)
{
    public string Fingerprint => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{Source}:{ExternalId}"))).ToLowerInvariant();
}
```

`src/Coldstart.Application/Harvest/NicheOptions.cs`:

```csharp
namespace Coldstart.Application.Harvest;

public sealed class NicheOptions
{
    public string Offer { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string SenderTitle { get; set; } = "";
    public List<string> TargetKeywords { get; set; } = [];
    public List<string> BuyingSignals { get; set; } = [];
    public List<string> Disqualifiers { get; set; } = [];
    public double MinScore { get; set; } = 0.4;
    public int MaxEnrichPerRun { get; set; } = 60;
    public HackerNewsOptions HackerNews { get; set; } = new();
    public FundingOptions Funding { get; set; } = new();
}

public sealed class HackerNewsOptions
{
    public bool Enabled { get; set; } = true;
    public List<string> Queries { get; set; } = [];
    public int MaxPerQuery { get; set; } = 30;
    public int LookbackHours { get; set; } = 336;
    public double ScoreWeight { get; set; } = 0.7;
}

public sealed class FundingOptions
{
    public bool Enabled { get; set; } = true;
    public List<string> Feeds { get; set; } = [];
    public int LookbackDays { get; set; } = 45;
    public int MaxPerFeed { get; set; } = 40;
    public bool RequireFundingTerms { get; set; } = true;
    public double BaseScore { get; set; } = 0.5;
}
```

`src/Coldstart.Application/Harvest/SignalResult.cs`:

```csharp
namespace Coldstart.Application.Harvest;

public sealed record SignalResult(double Score, IReadOnlyList<string> Matched);
```

`src/Coldstart.Application/Harvest/SignalScorer.cs` (port of `signals.py`):

```csharp
namespace Coldstart.Application.Harvest;

public static class SignalScorer
{
    public static SignalResult Score(RawItem item, NicheOptions niche, double baseScore, double scoreWeight)
    {
        var haystack = $"{item.Title}\n{item.Text}".ToLowerInvariant();
        var targetHits = niche.TargetKeywords.Where(k => haystack.Contains(k.ToLowerInvariant())).ToList();
        var signalHits = niche.BuyingSignals.Where(s => haystack.Contains(s.ToLowerInvariant())).ToList();

        var keywordScore = scoreWeight * (0.5 * Saturating(targetHits.Count) + 0.5 * Saturating(signalHits.Count));
        var score = Math.Round(Math.Max(keywordScore, baseScore), 3);
        return new SignalResult(score, [.. targetHits, .. signalHits]);
    }

    // 0 hits -> 0.0, 1 -> 0.6, 2 -> 0.8, 3 -> 0.9, capped at 1.0
    private static double Saturating(int n) => n <= 0 ? 0.0 : Math.Min(1.0, 1.0 - 0.4 / n);
}
```

- [ ] **Step 4: Run to green**

Run: `dotnet test tests/Coldstart.Application.Tests --filter SignalScorerTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Application/Harvest tests/Coldstart.Application.Tests/SignalScorerTests.cs
git commit -m "feat(harvest): RawItem, NicheOptions, deterministic SignalScorer"
```

---

### Task 2: Lead provenance + fingerprint dedup + migration

**Files:**
- Modify: `src/Coldstart.Domain/Lead.cs`, `src/Coldstart.Application/Ports/ILeadRepository.cs`, `src/Coldstart.Infrastructure/Repositories/LeadRepository.cs`, `src/Coldstart.Infrastructure/Configurations/LeadConfiguration.cs`
- Create migration: `AddLeadProvenance`
- Test: `tests/Coldstart.Domain.Tests/LeadHarvestTests.cs`, extend `tests/Coldstart.Infrastructure.Tests/LeadRepositoryTests.cs`

**Interfaces:**
- Produces:
  - `Lead` gains `Source`, `SourceUrl`, `Fingerprint`, `Score` (double), `MatchedSignals` (string, `private set`) and `static Lead CreateHarvested(CustomerId customerId, string company, Fit fit, string subject, string draft, string source, string sourceUrl, string fingerprint, double score, string matchedSignals, DateTimeOffset createdAt)`.
  - `ILeadRepository.ExistsByFingerprintAsync(string fingerprint, CancellationToken ct) : Task<bool>`.

- [ ] **Step 1: Write the failing domain + repo tests**

`tests/Coldstart.Domain.Tests/LeadHarvestTests.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class LeadHarvestTests
{
    [Fact]
    public void CreateHarvested_SetsProvenanceAndPendingReview()
    {
        // Act
        var lead = Lead.CreateHarvested(CustomerId.New(), "Patronus AI", Fit.Yes, "Subj", "Body",
            "hackernews", "https://news.ycombinator.com/item?id=1", "abc123", 0.72, "hiring,outbound",
            DateTimeOffset.UtcNow);

        // Assert
        lead.Source.Should().Be("hackernews");
        lead.Fingerprint.Should().Be("abc123");
        lead.Score.Should().Be(0.72);
        lead.MatchedSignals.Should().Be("hiring,outbound");
        lead.Status.Should().Be(LeadStatus.PendingReview);
    }
}
```

Add to `tests/Coldstart.Infrastructure.Tests/LeadRepositoryTests.cs`:

```csharp
    [Fact]
    public async Task ExistsByFingerprintAsync_TrueOnlyAfterSaved()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        var repo = new LeadRepository(db);
        var customer = Customer.Create("Coldstart");
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var lead = Lead.CreateHarvested(customer.Id, "Acme", Fit.Yes, "s", "b",
            "funding", "https://x/y", "fp-unique-1", 0.5, "raised", DateTimeOffset.UtcNow);

        // Act
        var before = await repo.ExistsByFingerprintAsync("fp-unique-1", CancellationToken.None);
        await repo.AddAsync(lead, CancellationToken.None);
        var after = await repo.ExistsByFingerprintAsync("fp-unique-1", CancellationToken.None);

        // Assert
        before.Should().BeFalse();
        after.Should().BeTrue();
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Domain.Tests --filter LeadHarvestTests`
Expected: FAIL — `CreateHarvested` missing.

- [ ] **Step 3: Extend `Lead`**

In `src/Coldstart.Domain/Lead.cs` add properties (after `SentAt`):

```csharp
    public string Source { get; private set; } = "";
    public string SourceUrl { get; private set; } = "";
    public string Fingerprint { get; private set; } = "";
    public double Score { get; private set; }
    public string MatchedSignals { get; private set; } = "";
```

Add the factory (after `Create`):

```csharp
    public static Lead CreateHarvested(CustomerId customerId, string company, Fit fit,
        string subject, string draft, string source, string sourceUrl, string fingerprint,
        double score, string matchedSignals, DateTimeOffset createdAt)
    {
        var lead = new Lead(LeadId.New(), customerId, company, fit, subject, draft, createdAt)
        {
            Source = source,
            SourceUrl = sourceUrl,
            Fingerprint = fingerprint,
            Score = score,
            MatchedSignals = matchedSignals,
        };
        return lead;
    }
```

(The private ctor already sets `Status = PendingReview`, `EmailConfidence = None`.)

- [ ] **Step 4: Add the repository port method + implementation**

In `src/Coldstart.Application/Ports/ILeadRepository.cs` add:

```csharp
    Task<bool> ExistsByFingerprintAsync(string fingerprint, CancellationToken ct);
```

In `src/Coldstart.Infrastructure/Repositories/LeadRepository.cs` add:

```csharp
    public async Task<bool> ExistsByFingerprintAsync(string fingerprint, CancellationToken ct) =>
        await db.Leads.AnyAsync(l => l.Fingerprint == fingerprint && fingerprint != "", ct);
```

- [ ] **Step 5: Map provenance + unique index**

In `src/Coldstart.Infrastructure/Configurations/LeadConfiguration.cs`, inside `Configure`, add:

```csharp
        builder.Property(l => l.Source);
        builder.Property(l => l.SourceUrl);
        builder.Property(l => l.Fingerprint);
        builder.Property(l => l.Score);
        builder.Property(l => l.MatchedSignals);
        builder.HasIndex(l => l.Fingerprint)
            .IsUnique()
            .HasFilter("\"Fingerprint\" <> ''");   // seeded/mock leads have '' — don't collide
```

- [ ] **Step 6: Migration**

```bash
dotnet ef migrations add AddLeadProvenance -p src/Coldstart.Infrastructure -s src/Coldstart.Infrastructure -o Migrations
```
Verify it adds the 5 columns + the filtered unique index on `leads`, nothing else.

- [ ] **Step 7: Run tests to green, build, commit**

Run: `docker compose up -d db && dotnet test tests/Coldstart.Domain.Tests tests/Coldstart.Infrastructure.Tests --filter "LeadHarvest|ExistsByFingerprint"` → PASS; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Domain src/Coldstart.Application src/Coldstart.Infrastructure tests
git commit -m "feat(harvest): Lead provenance + fingerprint dedup + migration"
```

---

### Task 3: ILeadSource port + HackerNewsSource

**Files:**
- Create: `src/Coldstart.Application/Harvest/ILeadSource.cs`, `src/Coldstart.Infrastructure/Harvest/HackerNewsSource.cs`
- Test: `tests/Coldstart.Infrastructure.Tests/HackerNewsSourceTests.cs`

**Interfaces:**
- Produces:
  - `interface ILeadSource { string Name { get; } double BaseScore { get; } double ScoreWeight { get; } bool Enabled { get; } Task<IReadOnlyList<RawItem>> FetchAsync(CancellationToken ct); }`
  - `HackerNewsSource(HttpClient http, NicheOptions niche)` — Algolia HN search; `Name="hackernews"`, `BaseScore=0`, `ScoreWeight=niche.HackerNews.ScoreWeight`.

- [ ] **Step 1: Write the failing test (parse a captured Algolia payload via a stub handler)**

`tests/Coldstart.Infrastructure.Tests/HackerNewsSourceTests.cs`:

```csharp
using System.Net;
using System.Text;
using Coldstart.Application.Harvest;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class HackerNewsSourceTests
{
    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    [Fact]
    public async Task FetchAsync_MapsHitsToRawItems_StrippingHtml()
    {
        // Arrange
        const string json = """
        {"hits":[{"objectID":"42","comment_text":"We are <b>hiring</b> an SDR &amp; more",
          "story_title":"Ask HN: Who is hiring?","author":"jane","created_at":"2026-07-01T00:00:00Z"}]}
        """;
        var http = new HttpClient(new StubHandler(json));
        var niche = new NicheOptions { HackerNews = new() { Queries = ["hiring"], ScoreWeight = 0.7 } };
        var sut = new HackerNewsSource(http, niche);

        // Act
        var items = await sut.FetchAsync(CancellationToken.None);

        // Assert
        items.Should().ContainSingle();
        items[0].Source.Should().Be("hackernews");
        items[0].ExternalId.Should().Be("42");
        items[0].Text.Should().Be("We are hiring an SDR & more");   // tags stripped, entities decoded
        items[0].Url.Should().Be("https://news.ycombinator.com/item?id=42");
        sut.ScoreWeight.Should().Be(0.7);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter HackerNewsSourceTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement port + adapter** (port of `sources/hackernews.py`)

`src/Coldstart.Application/Harvest/ILeadSource.cs`:

```csharp
namespace Coldstart.Application.Harvest;

public interface ILeadSource
{
    string Name { get; }
    double BaseScore { get; }
    double ScoreWeight { get; }
    bool Enabled { get; }
    Task<IReadOnlyList<RawItem>> FetchAsync(CancellationToken ct);
}
```

`src/Coldstart.Infrastructure/Harvest/HackerNewsSource.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

internal sealed class HackerNewsSource(HttpClient http, NicheOptions niche) : ILeadSource
{
    private const string Algolia = "https://hn.algolia.com/api/v1/search_by_date";
    private readonly HackerNewsOptions _o = niche.HackerNews;

    public string Name => "hackernews";
    public double BaseScore => 0;
    public double ScoreWeight => _o.ScoreWeight;
    public bool Enabled => _o.Enabled;

    public async Task<IReadOnlyList<RawItem>> FetchAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-_o.LookbackHours).ToUnixTimeSeconds();
        var items = new List<RawItem>();
        foreach (var query in _o.Queries)
        {
            var url = $"{Algolia}?query={Uri.EscapeDataString(query)}&tags=comment" +
                      $"&numericFilters=created_at_i>{cutoff}&hitsPerPage={_o.MaxPerQuery}";
            JsonElement root;
            try
            {
                root = await http.GetFromJsonAsync<JsonElement>(url, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                continue;   // one bad query must not kill the run
            }
            if (!root.TryGetProperty("hits", out var hits)) continue;
            foreach (var h in hits.EnumerateArray())
            {
                var text = Str(h, "comment_text");
                if (string.IsNullOrEmpty(text)) text = Str(h, "story_text");
                if (string.IsNullOrEmpty(text)) continue;
                var oid = Str(h, "objectID");
                items.Add(new RawItem("hackernews", oid, Str(h, "story_title"), StripHtml(text),
                    Str(h, "author"), $"https://news.ycombinator.com/item?id={oid}", Str(h, "created_at")));
            }
        }
        return items;
    }

    private static string Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string StripHtml(string s)
    {
        s = Regex.Replace(s, "<[^>]+>", " ");
        s = HttpUtility.HtmlDecode(s);
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
```

(Add `<FrameworkReference Include="Microsoft.AspNetCore.App" />`? No — `System.Web.HttpUtility` is in `System.Web.HttpUtility` assembly, available in .NET. If it doesn't resolve, use `System.Net.WebUtility.HtmlDecode` instead — same behavior.)

- [ ] **Step 4: Run to green, build, commit**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter HackerNewsSourceTests` → PASS; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Application/Harvest/ILeadSource.cs src/Coldstart.Infrastructure/Harvest/HackerNewsSource.cs tests/Coldstart.Infrastructure.Tests/HackerNewsSourceTests.cs
git commit -m "feat(harvest): ILeadSource + HackerNews Algolia adapter"
```

---

### Task 4: FundingRssSource

**Files:**
- Create: `src/Coldstart.Infrastructure/Harvest/FundingRssSource.cs`
- Test: `tests/Coldstart.Infrastructure.Tests/FundingRssSourceTests.cs`

**Interfaces:**
- Consumes: `ILeadSource`, `NicheOptions.Funding`.
- Produces: `FundingRssSource(HttpClient http, NicheOptions niche)` — `Name="funding"`, `BaseScore=niche.Funding.BaseScore`, `ScoreWeight=1`. Prepends the honest funding sentinel; filters by funding terms when configured; drops items older than `LookbackDays` (keeps undated).

- [ ] **Step 1: Write the failing test (parse an RSS fixture, funding-term filter)**

`tests/Coldstart.Infrastructure.Tests/FundingRssSourceTests.cs`:

```csharp
using System.Net;
using System.Text;
using Coldstart.Application.Harvest;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class FundingRssSourceTests
{
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/xml") });
    }

    private const string Rss = """
    <rss><channel>
      <item><title>Acme raises $12M Series A</title><link>https://x/acme</link>
        <description>Acme, a B2B startup, raised a round led by VC.</description>
        <guid>acme-1</guid><pubDate>Wed, 01 Jul 2026 10:00:00 GMT</pubDate></item>
      <item><title>Opinion: why VC is broken</title><link>https://x/op</link>
        <description>An editorial with no round.</description>
        <guid>op-1</guid><pubDate>Wed, 01 Jul 2026 10:00:00 GMT</pubDate></item>
    </channel></rss>
    """;

    [Fact]
    public async Task FetchAsync_KeepsFundingItems_DropsEditorial_AndPrependsSentinel()
    {
        // Arrange
        var http = new HttpClient(new StubHandler(Rss));
        var niche = new NicheOptions { Funding = new() { Feeds = ["https://x/feed"], RequireFundingTerms = true, LookbackDays = 100000 } };
        var sut = new FundingRssSource(http, niche);

        // Act
        var items = await sut.FetchAsync(CancellationToken.None);

        // Assert
        items.Should().ContainSingle();
        items[0].Title.Should().Contain("Acme raises");
        items[0].Text.Should().StartWith("FUNDING SIGNAL");
        items[0].Source.Should().Be("funding");
        sut.BaseScore.Should().Be(0.5);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter FundingRssSourceTests`
Expected: FAIL — type missing.

- [ ] **Step 3: Implement** (port of `sources/funding.py`, using `System.Xml.Linq`)

`src/Coldstart.Infrastructure/Harvest/FundingRssSource.cs`:

```csharp
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

internal sealed class FundingRssSource(HttpClient http, NicheOptions niche) : ILeadSource
{
    private const string Sentinel =
        "FUNDING SIGNAL — this company recently announced new investment, a strong " +
        "indicator they are about to scale headcount and invest in new tooling:";
    private static readonly Regex FundingRe = new(
        @"\brais(?:e|es|ed|ing)\b|\b(?:pre-)?seed\b|\bseries\s+[a-h]\b|\bfunding\b|\bround\b|\bled\sby\b|\$\d+(?:\.\d+)?\s*[mMbB]\b|\blands?\s+\$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly FundingOptions _o = niche.Funding;

    public string Name => "funding";
    public double BaseScore => _o.BaseScore;
    public double ScoreWeight => 1.0;
    public bool Enabled => _o.Enabled;

    public async Task<IReadOnlyList<RawItem>> FetchAsync(CancellationToken ct)
    {
        var items = new List<RawItem>();
        foreach (var feed in _o.Feeds)
        {
            string xml;
            try { xml = await http.GetStringAsync(feed, ct); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { continue; }
            items.AddRange(ItemsFromFeed(xml, feed, DateTimeOffset.UtcNow));
        }
        return items;
    }

    private IEnumerable<RawItem> ItemsFromFeed(string xml, string feedUrl, DateTimeOffset now)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); } catch (System.Xml.XmlException) { yield break; }
        var cutoff = now.AddDays(-_o.LookbackDays);
        var label = Uri.TryCreate(feedUrl, UriKind.Absolute, out var u) ? u.Host : "feed";

        var entries = doc.Descendants().Where(e => Local(e.Name) is "item" or "entry").Take(_o.MaxPerFeed);
        foreach (var node in entries)
        {
            string title = "", url = "", summary = "", ident = "";
            DateTimeOffset? published = null;
            foreach (var c in node.Elements())
            {
                switch (Local(c.Name))
                {
                    case "title": title = Clean(c.Value); break;
                    case "link": url = string.IsNullOrWhiteSpace(c.Value) ? (c.Attribute("href")?.Value ?? "") : c.Value.Trim(); break;
                    case "description" or "summary" or "content" when summary == "": summary = Clean(c.Value); break;
                    case "guid" or "id" when ident == "": ident = c.Value.Trim(); break;
                    case "pubdate" or "published" or "updated" when published is null: published = ParseDate(c.Value); break;
                }
            }
            if (published is not null && published < cutoff) continue;
            if (_o.RequireFundingTerms && !FundingRe.IsMatch($"{title} {summary}")) continue;
            var id = !string.IsNullOrEmpty(ident) ? ident : (!string.IsNullOrEmpty(url) ? url : title);
            yield return new RawItem("funding", $"{label}:{id}",
                title, $"{Sentinel} {title}. {summary}".Trim(), label, url,
                published?.ToString("o") ?? "");
        }
    }

    private static string Local(XName n) => n.LocalName.ToLowerInvariant();

    private static DateTimeOffset? ParseDate(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        if (DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            return dt;
        return null;
    }

    private static string Clean(string? s)
    {
        s = Regex.Replace(s ?? "", "<[^>]+>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(s), @"\s+", " ").Trim();
    }
}
```

- [ ] **Step 4: Run to green, build, commit**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter FundingRssSourceTests` → PASS; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Infrastructure/Harvest/FundingRssSource.cs tests/Coldstart.Infrastructure.Tests/FundingRssSourceTests.cs
git commit -m "feat(harvest): funding RSS/Atom source adapter"
```

---

### Task 5: ILeadEnricher + Groq adapter (qualify + draft)

**Files:**
- Create: `src/Coldstart.Application/Harvest/ILeadEnricher.cs`, `EnrichmentResult.cs`, `src/Coldstart.Infrastructure/Harvest/GroqLeadEnricher.cs`, `LlmOptions.cs`
- Test: `tests/Coldstart.Application.Tests/EnrichmentParsingTests.cs`

**Interfaces:**
- Produces:
  - `record EnrichmentResult(Fit Fit, string Summary, string Subject, string Draft, string Company)`.
  - `interface ILeadEnricher { Task<EnrichmentResult?> EnrichAsync(RawItem item, NicheOptions niche, SignalResult signal, CancellationToken ct); }` (null = failure/empty → caller skips).
  - `GroqLeadEnricher` implementing it; and `static EnrichmentResult? EnrichmentParser.Parse(string content)` (exposed for unit test).

- [ ] **Step 1: Write the failing parser test**

`tests/Coldstart.Application.Tests/EnrichmentParsingTests.cs`:

```csharp
using Coldstart.Application.Harvest;
using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class EnrichmentParsingTests
{
    [Fact]
    public void Parse_WellFormed_ExtractsFiveFields()
    {
        // Arrange
        const string content = """
        FIT: YES
        SUMMARY: A funded startup scaling sales.
        SUBJECT: Congrats on the raise
        DRAFT: Saw your round. One question about pipeline?
        COMPANY: Acme
        """;

        // Act
        var r = EnrichmentParser.Parse(content);

        // Assert
        r.Should().NotBeNull();
        r!.Fit.Should().Be(Fit.Yes);
        r.Subject.Should().Be("Congrats on the raise");
        r.Company.Should().Be("Acme");
    }

    [Fact]
    public void Parse_FitNo_StillParsesAsNo()
    {
        // Arrange
        const string content = "FIT: NO\nSUMMARY: Job seeker.\nSUBJECT: -\nDRAFT: -\nCOMPANY: -";

        // Act
        var r = EnrichmentParser.Parse(content);

        // Assert
        r!.Fit.Should().Be(Fit.No);
    }

    [Fact]
    public void Parse_MissingFitLine_ReturnsNull()
    {
        // Act
        var r = EnrichmentParser.Parse("garbage with no labels");

        // Assert
        r.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Application.Tests --filter EnrichmentParsingTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement port, result, parser, Groq adapter**

`src/Coldstart.Application/Harvest/EnrichmentResult.cs`:

```csharp
using Coldstart.Domain;

namespace Coldstart.Application.Harvest;

public sealed record EnrichmentResult(Fit Fit, string Summary, string Subject, string Draft, string Company);

public static class EnrichmentParser
{
    public static EnrichmentResult? Parse(string content)
    {
        string Field(string label)
        {
            foreach (var line in content.Split('\n'))
            {
                var t = line.TrimStart();
                if (t.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase))
                    return t[(label.Length + 1)..].Trim();
            }
            return "";
        }

        var fitRaw = Field("FIT").ToUpperInvariant();
        Fit? fit = fitRaw.StartsWith("YES") ? Fit.Yes : fitRaw.StartsWith("MAYBE") ? Fit.Maybe : fitRaw.StartsWith("NO") ? Fit.No : null;
        if (fit is null) return null;   // no parseable FIT line → treat as failure

        return new EnrichmentResult(fit.Value, Field("SUMMARY"), Field("SUBJECT"), Field("DRAFT"), Field("COMPANY"));
    }
}
```

`src/Coldstart.Application/Harvest/ILeadEnricher.cs`:

```csharp
namespace Coldstart.Application.Harvest;

public interface ILeadEnricher
{
    Task<EnrichmentResult?> EnrichAsync(RawItem item, NicheOptions niche, SignalResult signal, CancellationToken ct);
}
```

`src/Coldstart.Infrastructure/Harvest/LlmOptions.cs`:

```csharp
namespace Coldstart.Infrastructure.Harvest;

public sealed class LlmOptions
{
    public string Provider { get; set; } = "groq";
    public string Model { get; set; } = "llama-3.3-70b-versatile";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/chat/completions";
}
```

`src/Coldstart.Infrastructure/Harvest/GroqLeadEnricher.cs` (prompt ported from `enrich.py`):

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Harvest;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Harvest;

internal sealed class GroqLeadEnricher(HttpClient http, IOptions<LlmOptions> options) : ILeadEnricher
{
    private const string System =
        "You are a top-performing B2B SDR known for replies, not blasts. You read one piece of " +
        "public web content, judge honestly whether the author/company is a real fit for a " +
        "specific offer, and if so write outreach so specific it could not have been sent to " +
        "anyone else. You never invent facts. You would rather say NO than send a generic pitch.";
    private readonly LlmOptions _o = options.Value;

    public async Task<EnrichmentResult?> EnrichAsync(RawItem item, NicheOptions niche, SignalResult signal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_o.ApiKey)) return null;
        var prompt = BuildPrompt(item, niche, signal);
        var body = new
        {
            model = _o.Model,
            temperature = 0.4,
            messages = new[]
            {
                new { role = "system", content = System },
                new { role = "user", content = prompt },
            },
        };
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _o.BaseUrl)
            { Content = JsonContent.Create(body) };
            req.Headers.Add("Authorization", $"Bearer {_o.ApiKey}");
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            var content = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return string.IsNullOrWhiteSpace(content) ? null : EnrichmentParser.Parse(content);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException)
        {
            return null;   // rate limit / transient → skip (retried next run), never crash the batch
        }
    }

    // Ported from enrich.py PROMPT_TEMPLATE.
    private static string BuildPrompt(RawItem item, NicheOptions n, SignalResult signal) =>
        $"""
        OFFER WE SELL:
        {n.Offer}

        WHO IS A GOOD FIT:
        (niche described by keywords: {string.Join(", ", n.TargetKeywords)})

        WHO IS NOT A FIT — mark FIT: NO if ANY of these apply, even when keywords match:
        {string.Join("\n", n.Disqualifiers.Select(d => "- " + d))}

        HARVESTED CONTENT (source: {item.Source} · author: {item.Author}):
        \"\"\"
        {item.Text}
        \"\"\"

        Pre-detected signals (keyword-level, may be noisy): {string.Join(", ", signal.Matched)}

        Return EXACTLY these five sections, each on its own line with the label:

        FIT: YES | MAYBE | NO
        SUMMARY: one sentence — who they are and the concrete reason they do or don't fit.
        SUBJECT: a 4-7 word subject line referencing their specific situation (no clickbait, no emojis).
        DRAFT: 3-4 sentences from {n.SenderName} ({n.SenderTitle}). Open with a SPECIFIC detail from the content, connect it to one concrete way the offer helps, end with one low-friction question. No greeting or signature. Banned: "I hope this email finds you well", "reaching out", "game-changer", "circle back", "touch base".
        COMPANY: the real company/org name this is about, or "-" if it cannot be determined. Never guess.

        If FIT is NO, still fill SUBJECT and DRAFT with "-".
        """;
}
```

- [ ] **Step 4: Run parser test to green, build, commit**

Run: `dotnet test tests/Coldstart.Application.Tests --filter EnrichmentParsingTests` → PASS; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Application/Harvest/ILeadEnricher.cs src/Coldstart.Application/Harvest/EnrichmentResult.cs src/Coldstart.Infrastructure/Harvest/GroqLeadEnricher.cs src/Coldstart.Infrastructure/Harvest/LlmOptions.cs tests/Coldstart.Application.Tests/EnrichmentParsingTests.cs
git commit -m "feat(harvest): ILeadEnricher + Groq adapter (qualify + draft) + parser"
```

---

### Task 6: HarvestLeads orchestrator + house customer + DI + end-to-end test

**Files:**
- Create: `src/Coldstart.Application/Harvest/IHarvestLeads.cs`, `HarvestReport.cs`, `HarvestLeads.cs`
- Modify: `src/Coldstart.Application/DependencyInjection.cs`, `src/Coldstart.Infrastructure/DependencyInjection.cs`
- Test: `tests/Coldstart.Application.Tests/HarvestLeadsTests.cs` (fakes) and `tests/Coldstart.Infrastructure.Tests` end-to-end (Testcontainers)

**Interfaces:**
- Consumes: `IEnumerable<ILeadSource>`, `ILeadEnricher`, `ILeadRepository`, `ICustomerRepository`, `NicheOptions`.
- Produces: `interface IHarvestLeads { Task<HarvestReport> RunAsync(CancellationToken ct) }`; `record HarvestReport(int Harvested, int ScoredIn, int Deduped, int Enriched, int Saved)`.

- [ ] **Step 1: Write the failing orchestration test (fakes; asserts the full filter chain)**

`tests/Coldstart.Application.Tests/HarvestLeadsTests.cs`:

```csharp
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
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Application.Tests --filter HarvestLeadsTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement the orchestrator** (port of `pipeline.run`)

`src/Coldstart.Application/Harvest/HarvestReport.cs`:

```csharp
namespace Coldstart.Application.Harvest;

public sealed record HarvestReport(int Harvested, int ScoredIn, int Deduped, int Enriched, int Saved);
```

`src/Coldstart.Application/Harvest/IHarvestLeads.cs`:

```csharp
namespace Coldstart.Application.Harvest;

public interface IHarvestLeads
{
    Task<HarvestReport> RunAsync(CancellationToken ct);
}
```

`src/Coldstart.Application/Harvest/HarvestLeads.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Harvest;

internal sealed class HarvestLeads(
    IEnumerable<ILeadSource> sources,
    ILeadEnricher enricher,
    ILeadRepository leads,
    ICustomerRepository customers,
    NicheOptions niche) : IHarvestLeads
{
    private const string HouseCustomerName = "Coldstart";

    public async Task<HarvestReport> RunAsync(CancellationToken ct)
    {
        int harvested = 0, scoredIn = 0, deduped = 0, enriched = 0, saved = 0;

        // Phase 1: harvest + cheap score gate + dedup (no LLM spend yet).
        var candidates = new List<(RawItem item, SignalResult signal)>();
        foreach (var source in sources.Where(s => s.Enabled))
        {
            IReadOnlyList<RawItem> items;
            try { items = await source.FetchAsync(ct); }
            catch { continue; }   // a failing source must not abort the run
            foreach (var item in items)
            {
                harvested++;
                var signal = SignalScorer.Score(item, niche, source.BaseScore, source.ScoreWeight);
                if (signal.Score < niche.MinScore) continue;
                scoredIn++;
                if (await leads.ExistsByFingerprintAsync(item.Fingerprint, ct)) { deduped++; continue; }
                candidates.Add((item, signal));
            }
        }

        // Phase 2: enrich only the top-N by score (spend cap), save real fits.
        var house = await ResolveHouseCustomerAsync(ct);
        foreach (var (item, signal) in candidates.OrderByDescending(c => c.signal.Score).Take(niche.MaxEnrichPerRun))
        {
            var result = await enricher.EnrichAsync(item, niche, signal, ct);
            if (result is null) continue;         // LLM failed/empty → retried next run
            enriched++;
            if (result.Fit == Fit.No) continue;   // qualified out
            var company = string.IsNullOrWhiteSpace(result.Company) || result.Company == "-" ? item.Author : result.Company;
            var lead = Lead.CreateHarvested(house.Id, company, result.Fit, result.Subject, result.Draft,
                item.Source, item.Url, item.Fingerprint, signal.Score, string.Join(",", signal.Matched), DateTimeOffset.UtcNow);
            await leads.AddAsync(lead, ct);
            saved++;
        }

        return new HarvestReport(harvested, scoredIn, deduped, enriched, saved);
    }

    private async Task<Customer> ResolveHouseCustomerAsync(CancellationToken ct)
    {
        var existing = (await customers.AllAsync(ct)).FirstOrDefault(c => c.Name == HouseCustomerName);
        if (existing is not null) return existing;
        var created = Customer.Create(HouseCustomerName);
        await customers.AddAsync(created, ct);
        return created;
    }
}
```

Register in `src/Coldstart.Application/DependencyInjection.cs` (`AddApplication`): `services.AddScoped<Harvest.IHarvestLeads, Harvest.HarvestLeads>();`

- [ ] **Step 4: Run unit tests to green**

Run: `dotnet test tests/Coldstart.Application.Tests --filter HarvestLeadsTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Register Infrastructure adapters + HttpClient**

In `src/Coldstart.Infrastructure/DependencyInjection.cs` (`AddInfrastructure`), add:

```csharp
        services.AddHttpClient();
        services.AddScoped<Application.Harvest.ILeadSource, Harvest.HackerNewsSource>();
        services.AddScoped<Application.Harvest.ILeadSource, Harvest.FundingRssSource>();
        services.AddScoped<Application.Harvest.ILeadEnricher, Harvest.GroqLeadEnricher>();
```

(`HackerNewsSource`/`FundingRssSource`/`GroqLeadEnricher` take `HttpClient` — `AddHttpClient()` provides `IHttpClientFactory`; inject `HttpClient` via `services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient())` OR change the adapters to take `IHttpClientFactory`. Use the factory-per-adapter form to avoid captive-dependency issues: add `services.AddHttpClient<Harvest.HackerNewsSource>();` etc. — the typed-client registration also registers the type, so register the port to resolve the concrete: keep `AddScoped<ILeadSource, HackerNewsSource>()` and `AddHttpClient<HackerNewsSource>()`.)

`NicheOptions` + `LlmOptions` are bound from config in the composition roots (Web/Harvester) — Task 7.

- [ ] **Step 6: End-to-end integration test (Testcontainers, fake source + fake enricher)**

`tests/Coldstart.Infrastructure.Tests/HarvestEndToEndTests.cs`:

```csharp
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
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<CancellationToken>())
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
```

- [ ] **Step 7: Run all tests to green, build, commit**

Run: `docker compose up -d db && dotnet test Coldstart.sln` → all PASS; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Application src/Coldstart.Infrastructure tests
git commit -m "feat(harvest): HarvestLeads orchestrator + house customer + DI + e2e test"
```

---

### Task 7: Console runner + config binding + on-demand run

**Files:**
- Create: `src/Coldstart.Harvester/Coldstart.Harvester.csproj`, `Program.cs`, `appsettings.json`
- Modify: `Coldstart.sln` (add project), `src/Coldstart.Web/README.md` (Groq key + run instructions)

**Interfaces:**
- Consumes: `AddApplication`, `AddInfrastructure`, `IHarvestLeads`, `NicheOptions`, `LlmOptions`.
- Produces: `dotnet run --project src/Coldstart.Harvester` → runs one harvest, prints the `HarvestReport`.

- [ ] **Step 1: Create the console project + add to solution**

```bash
cd /Users/andrzej.lech/Code/private/lead-engine
dotnet new console -n Coldstart.Harvester -o src/Coldstart.Harvester -f net10.0
dotnet sln add src/Coldstart.Harvester
dotnet add src/Coldstart.Harvester reference src/Coldstart.Application src/Coldstart.Infrastructure
dotnet add src/Coldstart.Harvester package Microsoft.Extensions.Hosting
dotnet add src/Coldstart.Harvester package Microsoft.Extensions.Configuration.UserSecrets
dotnet user-secrets init --project src/Coldstart.Harvester
```

- [ ] **Step 2: `Program.cs` — build host, bind config, run harvest**

`src/Coldstart.Harvester/Program.cs`:

```csharp
using Coldstart.Application;
using Coldstart.Application.Harvest;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Harvest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets<Program>(optional: true);

var conn = builder.Configuration.GetConnectionString("Coldstart")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Coldstart");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(conn);
builder.Services.AddSingleton(sp =>
{
    var n = new NicheOptions();
    sp.GetRequiredService<IConfiguration>().GetSection("Niche").Bind(n);
    return n;
});
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("Llm"));

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var harvest = scope.ServiceProvider.GetRequiredService<IHarvestLeads>();
var report = await harvest.RunAsync(CancellationToken.None);
Console.WriteLine($"harvested={report.Harvested} scored_in={report.ScoredIn} deduped={report.Deduped} " +
                  $"enriched={report.Enriched} saved={report.Saved}");
```

- [ ] **Step 3: `appsettings.json` — the Coldstart niche (ported from `clients/coldstart.yaml`)**

`src/Coldstart.Harvester/appsettings.json` (set `"Copy to output" = PreserveNewest` in the csproj; add `<None Update="appsettings.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>`):

```json
{
  "ConnectionStrings": { "Coldstart": "Host=localhost;Port=5432;Database=coldstart;Username=coldstart;Password=localdev" },
  "Llm": { "Provider": "groq", "Model": "llama-3.3-70b-versatile" },
  "Niche": {
    "Offer": "Coldstart is a done-for-you lead engine: every morning it finds B2B companies showing a buying signal right now, qualifies them, and writes the first outreach email. You approve and it sends.",
    "SenderName": "Andrzej",
    "SenderTitle": "Founder, Coldstart",
    "TargetKeywords": ["SDR","BDR","sales development","account executive","head of sales","head of growth","VP Sales","demand generation","outbound","pipeline","go-to-market"],
    "BuyingSignals": ["hiring","we're growing","scaling","raised","seed","series a","looking for","founder"],
    "Disqualifiers": [
      "The company itself sells lead generation, sales-outreach, prospecting, appointment-setting, or cold-email software/services (a direct competitor)",
      "An anonymous forum/comment author venting or sharing an opinion, not a named decision-maker at an identifiable company",
      "A job seeker or candidate looking for a role",
      "A large enterprise with an established outbound team unlikely to buy from a solo operator"
    ],
    "MinScore": 0.4,
    "MaxEnrichPerRun": 60,
    "HackerNews": { "Enabled": true, "Queries": ["hiring sales development","hiring BDR","head of growth","outbound sales","we are hiring sales"], "MaxPerQuery": 30, "LookbackHours": 336, "ScoreWeight": 0.7 },
    "Funding": { "Enabled": true, "Feeds": ["https://techcrunch.com/category/venture/feed/","https://tech.eu/feed/","https://www.eu-startups.com/feed/","https://news.crunchbase.com/feed/","https://techfundingnews.com/feed/"], "LookbackDays": 45, "MaxPerFeed": 40, "RequireFundingTerms": true, "BaseScore": 0.5 }
  }
}
```

The Groq key is a secret: `dotnet user-secrets set "Llm:ApiKey" "<groq-key>" --project src/Coldstart.Harvester`. The connection string for local runs is the same Postgres from `docker compose`.

- [ ] **Step 4: Build + run on demand (live smoke — manual)**

```bash
docker compose up -d db
dotnet run --project src/Coldstart.Harvester
# expect a line like: harvested=NN scored_in=NN deduped=NN enriched=NN saved=NN
```
Then log in to `/app` as the Coldstart house account (or query the DB) to see the saved leads. Build must be 0 warnings: `dotnet build Coldstart.sln`.

- [ ] **Step 5: Document + commit**

Append a "Harvesting leads" section to `src/Coldstart.Web/README.md`: how to set `Llm:ApiKey` (Groq) via user-secrets and run `dotnet run --project src/Coldstart.Harvester`.
```bash
git add src/Coldstart.Harvester Coldstart.sln src/Coldstart.Web/README.md
git commit -m "feat(harvest): console runner + Coldstart niche config + docs"
```

---

## Self-Review

**Spec coverage:**
- Harvest HN + funding → Tasks 3, 4. ✓
- Deterministic score gate → Task 1. ✓
- Dedup by fingerprint → Task 2 (+ used in Task 6). ✓
- LLM qualify+draft (Groq, disqualifiers, FIT/SUBJECT/DRAFT/COMPANY) → Task 5. ✓
- Save filtered leads under house customer, visible in /app → Task 6. ✓
- Runnable on demand → Task 7. ✓
- Niche config ported from coldstart.yaml → Task 7. ✓
- Testing (scorer/parser/orchestrator unit; e2e Testcontainers with fakes; live smoke manual) → throughout. ✓
- Non-goals (find/verify, send, multi-client, scheduling) → untouched. ✓

**Placeholder scan:** Port adapters reference the Python source files as the translation origin but include the full C# — no "implement later". The `System.Web` vs `System.Net.WebUtility` note gives a concrete fallback. Groq key + connection string are config/secret values, not code placeholders.

**Type consistency:** `RawItem(Source,ExternalId,Title,Text,Author,Url,CreatedAt)+Fingerprint`, `SignalScorer.Score(item,niche,baseScore,scoreWeight)→SignalResult(Score,Matched)`, `ILeadSource{Name,BaseScore,ScoreWeight,Enabled,FetchAsync}`, `ILeadEnricher.EnrichAsync(item,niche,signal,ct)→EnrichmentResult?(Fit,Summary,Subject,Draft,Company)`, `EnrichmentParser.Parse`, `Lead.CreateHarvested(...)`, `ILeadRepository.ExistsByFingerprintAsync`, `IHarvestLeads.RunAsync→HarvestReport(Harvested,ScoredIn,Deduped,Enriched,Saved)`, `NicheOptions`/`LlmOptions` — consistent across Tasks 1–7.

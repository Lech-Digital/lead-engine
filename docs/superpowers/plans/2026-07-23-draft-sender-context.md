# Draft Sender-Context Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give overnight-generated drafts a sender-company brief (admin-entered, AI-polished) and, for the operator's own account, a "found via Coldstart" dogfooding line — per `docs/superpowers/specs/2026-07-23-draft-sender-context-design.md`.

**Architecture:** Bottom-up — `CompanyProfile` gains `Brief` + `MentionSourceTool` (Domain) with EF columns + migration; the Application read/write plumbing carries them; a new `ICompanyBriefPolisher` (Groq adapter) polishes on demand; a `SenderContext` record threads from `HarvestLeads` (built from the target customer's profile) into `ILeadEnricher`/`GroqLeadEnricher`'s prompt; the `/app/profile` page gains the brief textarea, the toggle, and a "Polish with AI" button.

**Tech Stack:** .NET 10 Razor Pages, EF Core + Postgres, MailKit-free Groq HTTP (existing `LlmOptions`/HttpClient pattern), xUnit + FluentAssertions, Testcontainers Postgres.

## Global Constraints

- Hexagonal (CS-HEX): Domain dependency-free; ports/DTOs in Application; Groq adapters in Infrastructure; DI in composition roots only. `internal` impl classes, `public record` DTOs.
- `CompanyProfile` (domain record) and `CompanyProfileView`/`CompanyProfileInput` are POSITIONAL records — append new fields at the END and fix every construction site (`grep -rn "new CompanyProfile(\|new CompanyProfileView(\|new CompanyProfileInput(" src tests`).
- `ILeadEnricher.EnrichAsync` gains a `SenderContext sender` parameter immediately BEFORE `CancellationToken ct`; update every implementation and call site.
- Draft prompt additions are exact: a `WHO IS REACHING OUT` section only when the brief is non-empty; the Coldstart/source instruction only when `MentionSourceTool` is true.
- Polishing and enrichment are best-effort: missing `LlmOptions.ApiKey` or a failed call → return null / skip, never throw into the request (mirror `GroqLeadEnricher`).
- Async + CancellationToken to IO; no `.Result`/`.Wait()`.
- Tests xUnit + FluentAssertions, `MethodName_StateUnderTest_ExpectedBehavior`, AAA comments. Full-solution `dotnet test` green at the end of every task (report the real command + per-project totals). Docker running.
- EF migration friction: `dotnet ef` needs the Design package temporarily in `Coldstart.Web.csproj` — add, generate, then `git checkout` the csproj (verify zero diff). Snapshot is tool-regenerated, never hand-edited.
- Commits conventional, NO Co-Authored-By trailers. Stage only each task's listed paths — the repo has unrelated uncommitted user work; never stage it.

---

### Task 1: `CompanyProfile` fields + EF mapping + migration

**Files:**
- Modify: `src/Coldstart.Domain/CompanyProfile.cs`
- Modify: `src/Coldstart.Infrastructure/Configurations/CustomerConfiguration.cs`
- Create: `src/Coldstart.Infrastructure/Migrations/<timestamp>_AddCompanyBrief.cs` (+ `.Designer.cs`, generated)
- Modify: `src/Coldstart.Infrastructure/Migrations/ColdstartDbContextModelSnapshot.cs` (tool-regenerated)
- Test: `tests/Coldstart.Infrastructure.Tests/CustomerRepositoryTests.cs` (round-trip the new fields)

**Interfaces:**
- Consumes: nothing.
- Produces: `CompanyProfile(..., string? Brief, bool MentionSourceTool)` (7 existing + 2 new, in that order); `CompanyProfile.Empty` with `Brief=null, MentionSourceTool=false`. Owned columns `ProfileBrief` (text, null) + `ProfileMentionSourceTool` (bool, default false).

- [ ] **Step 1: Write the failing repository round-trip test**

Read `tests/Coldstart.Infrastructure.Tests/CustomerRepositoryTests.cs` + `PostgresFixture.cs` first; mirror the existing profile round-trip test. Add:

```csharp
[Fact]
public async Task GetAsync_AfterSavingBriefAndToggle_RoundTripsThem()
{
    // Arrange
    await using var ctx = _fixture.NewContext();
    var repo = new CustomerRepository(ctx);
    var customer = Customer.Create("brief-co@example.com");
    customer.UpdateProfile(new CompanyProfile("Acme", null, null, null, null, null, null,
        Brief: "We do X for Y.", MentionSourceTool: true));
    await repo.AddAsync(customer, CancellationToken.None);

    // Act — fresh context to force a real read
    await using var ctx2 = _fixture.NewContext();
    var reloaded = await new CustomerRepository(ctx2).GetAsync(customer.Id, CancellationToken.None);

    // Assert
    reloaded!.Profile.Brief.Should().Be("We do X for Y.");
    reloaded.Profile.MentionSourceTool.Should().BeTrue();
}
```

(Match the file's real context/repo construction + persistence helpers; positional args must include the two new trailing values.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~CustomerRepository"`
Expected: compile error — `CompanyProfile` has no `Brief`/`MentionSourceTool`.

- [ ] **Step 3: Add the domain fields**

`src/Coldstart.Domain/CompanyProfile.cs`:

```csharp
namespace Coldstart.Domain;

public sealed record CompanyProfile(
    string? CompanyName,
    string? Address,
    string? VatNumber,
    string? KvkNumber,
    string? Niche,
    string? Website,
    string? ContactEmail,
    string? Brief = null,
    bool MentionSourceTool = false)
{
    public static CompanyProfile Empty { get; } = new(null, null, null, null, null, null, null);
}
```

(Defaulted params keep existing `new CompanyProfile(7 args)` call sites compiling; still `grep -rn "new CompanyProfile("` to confirm none break.)

- [ ] **Step 4: Map the two columns**

In `CustomerConfiguration.Configure`, inside the `OwnsOne(c => c.Profile, p => { ... })` block, add:

```csharp
            p.Property(x => x.Brief).HasColumnName("ProfileBrief");
            p.Property(x => x.MentionSourceTool).HasColumnName("ProfileMentionSourceTool").HasDefaultValue(false);
```

- [ ] **Step 5: Generate the migration**

```bash
dotnet add src/Coldstart.Web package Microsoft.EntityFrameworkCore.Design
dotnet ef migrations add AddCompanyBrief --project src/Coldstart.Infrastructure --startup-project src/Coldstart.Web --output-dir Migrations
git checkout src/Coldstart.Web/Coldstart.Web.csproj
```

Confirm the generated `Up` adds `ProfileBrief` (text, nullable) + `ProfileMentionSourceTool` (boolean, default false) to `customers`, and `git status` shows no csproj change.

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~CustomerRepository"` → PASS. Full `dotnet test` → green (the Web suite applies the migration via its Testcontainer; model must have no pending changes).

- [ ] **Step 7: Commit**

```bash
git add src/Coldstart.Domain/CompanyProfile.cs src/Coldstart.Infrastructure/Configurations/CustomerConfiguration.cs src/Coldstart.Infrastructure/Migrations tests/Coldstart.Infrastructure.Tests/CustomerRepositoryTests.cs
git commit -m "feat(domain): company brief + mention-source toggle on CompanyProfile with EF migration"
```

---

### Task 2: Application plumbing — view/input/ports carry the two fields

**Files:**
- Modify: `src/Coldstart.Application/Customers/CompanyProfileView.cs`
- Modify: `src/Coldstart.Application/Customers/CompanyProfileInput.cs`
- Modify: `src/Coldstart.Application/Customers/GetCompanyProfile.cs`
- Modify: `src/Coldstart.Application/Customers/UpdateCompanyProfile.cs`
- Test: `tests/Coldstart.Application.Tests/` (the file testing profile get/update — mirror it; if none, add `CompanyProfileFlowTests.cs`)

**Interfaces:**
- Consumes: Task 1 `CompanyProfile.Brief`/`MentionSourceTool`.
- Produces: `CompanyProfileView(..., string? Brief, bool MentionSourceTool)` and `CompanyProfileInput(..., string? Brief, bool MentionSourceTool)` (appended); `IsEmpty` UNCHANGED (does not consider brief/toggle).

- [ ] **Step 1: Write/adjust the failing test**

Mirror the existing profile-update test (search `tests/Coldstart.Application.Tests` for `UpdateCompanyProfile`/`GetCompanyProfile`). Add:

```csharp
[Fact]
public async Task UpdateThenGet_CarriesBriefAndToggle()
{
    // Arrange — fake ICustomerRepository seeding a customer (mirror the file's helper)
    var input = new CompanyProfileInput("Acme", null, null, null, null, null, null,
        Brief: "We help dentists fill their calendar.", MentionSourceTool: true);

    // Act
    await update.UpdateAsync(customerId, input, CancellationToken.None);
    var view = await get.ForCustomerAsync(customerId, CancellationToken.None);

    // Assert
    view.Brief.Should().Be("We help dentists fill their calendar.");
    view.MentionSourceTool.Should().BeTrue();
}
```

- [ ] **Step 2: Run to verify failure** — compile error (records lack the fields).

- [ ] **Step 3: Implement**

`CompanyProfileView.cs`:

```csharp
namespace Coldstart.Application.Customers;

public sealed record CompanyProfileView(
    string? CompanyName, string? Address, string? VatNumber, string? KvkNumber,
    string? Niche, string? Website, string? ContactEmail,
    string? Brief = null, bool MentionSourceTool = false)
{
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(CompanyName) && string.IsNullOrWhiteSpace(Address)
        && string.IsNullOrWhiteSpace(VatNumber) && string.IsNullOrWhiteSpace(KvkNumber)
        && string.IsNullOrWhiteSpace(Niche) && string.IsNullOrWhiteSpace(Website)
        && string.IsNullOrWhiteSpace(ContactEmail);
}
```

`CompanyProfileInput.cs`:

```csharp
namespace Coldstart.Application.Customers;

public sealed record CompanyProfileInput(
    string? CompanyName, string? Address, string? VatNumber, string? KvkNumber,
    string? Niche, string? Website, string? ContactEmail,
    string? Brief = null, bool MentionSourceTool = false);
```

`GetCompanyProfile.cs` — map the two fields into the view:

```csharp
        return new CompanyProfileView(p.CompanyName, p.Address, p.VatNumber, p.KvkNumber,
            p.Niche, p.Website, p.ContactEmail, p.Brief, p.MentionSourceTool);
```

`UpdateCompanyProfile.cs` — carry them into the domain profile:

```csharp
        customer.UpdateProfile(new CompanyProfile(input.CompanyName, input.Address, input.VatNumber,
            input.KvkNumber, input.Niche, input.Website, input.ContactEmail, input.Brief, input.MentionSourceTool));
```

- [ ] **Step 4: Run tests** — filter passes; full `dotnet test` green.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Application/Customers tests/Coldstart.Application.Tests
git commit -m "feat(app): carry brief + mention-source toggle through profile view/input/ports"
```

---

### Task 3: `ICompanyBriefPolisher` + `GroqBriefPolisher`

**Files:**
- Create: `src/Coldstart.Application/Customers/ICompanyBriefPolisher.cs`
- Create: `src/Coldstart.Infrastructure/Harvest/GroqBriefPolisher.cs`
- Modify: `src/Coldstart.Infrastructure/DependencyInjection.cs` (register)
- Test: `tests/Coldstart.Infrastructure.Tests/GroqBriefPolisherTests.cs`

**Interfaces:**
- Consumes: existing `LlmOptions` + injected `HttpClient` (same as `GroqLeadEnricher`).
- Produces: `ICompanyBriefPolisher.PolishAsync(string rawBrief, CancellationToken ct) → Task<string?>` (polished text, or null when unavailable/failed). DI: `services.AddScoped<Application.Customers.ICompanyBriefPolisher, Harvest.GroqBriefPolisher>()`.

- [ ] **Step 1: Write the failing tests**

Read `tests/Coldstart.Infrastructure.Tests/GroqLeadEnricherTests.cs` first and reuse its `SequenceHandler`/`Ok`/`Opts()` test seams. Create `GroqBriefPolisherTests.cs`:

```csharp
using System.Net;
using System.Text;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class GroqBriefPolisherTests
{
    private const string Ok = """{"choices":[{"message":{"content":"Acme helps dentists fill their calendars with AI-driven scheduling."}}]}""";

    private sealed class StubHandler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static IOptions<LlmOptions> Opts(string key = "test-key") => Options.Create(new LlmOptions { ApiKey = key });

    [Fact]
    public async Task PolishAsync_OnSuccess_ReturnsPolishedText()
    {
        // Arrange
        var sut = new GroqBriefPolisher(new HttpClient(new StubHandler(HttpStatusCode.OK, Ok)), Opts());

        // Act
        var result = await sut.PolishAsync("we do scheduling for dentists", CancellationToken.None);

        // Assert
        result.Should().Contain("Acme helps dentists");
    }

    [Fact]
    public async Task PolishAsync_WithNoApiKey_ReturnsNull()
    {
        // Arrange
        var sut = new GroqBriefPolisher(new HttpClient(new StubHandler(HttpStatusCode.OK, Ok)), Opts(key: ""));

        // Act
        var result = await sut.PolishAsync("anything", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task PolishAsync_OnHttpError_ReturnsNull()
    {
        // Arrange
        var sut = new GroqBriefPolisher(new HttpClient(new StubHandler(HttpStatusCode.InternalServerError, "")), Opts());

        // Act
        var result = await sut.PolishAsync("anything", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify failure** — compile error (`GroqBriefPolisher` missing).

- [ ] **Step 3: Implement**

`src/Coldstart.Application/Customers/ICompanyBriefPolisher.cs`:

```csharp
namespace Coldstart.Application.Customers;

public interface ICompanyBriefPolisher
{
    // Returns a tightened brief, or null if polishing is unavailable/failed (caller keeps the raw text).
    Task<string?> PolishAsync(string rawBrief, CancellationToken ct);
}
```

`src/Coldstart.Infrastructure/Harvest/GroqBriefPolisher.cs` (model the request/response + failure handling on `GroqLeadEnricher`; no retry loop needed — one best-effort call):

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Customers;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Harvest;

internal sealed class GroqBriefPolisher(HttpClient http, IOptions<LlmOptions> options) : ICompanyBriefPolisher
{
    private const string System =
        "You tighten a founder's rough notes about their own company into a crisp 2-3 sentence " +
        "third-person brief usable as context in a cold outreach email. Never invent facts, " +
        "numbers, or claims not present in the input. Return ONLY the brief — no preamble, no quotes.";

    private readonly LlmOptions _o = options.Value;

    public async Task<string?> PolishAsync(string rawBrief, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_o.ApiKey) || string.IsNullOrWhiteSpace(rawBrief)) return null;
        var body = new
        {
            model = _o.Model,
            temperature = 0.3,
            messages = new[]
            {
                new { role = "system", content = System },
                new { role = "user", content = rawBrief },
            },
        };
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _o.BaseUrl) { Content = JsonContent.Create(body) };
            req.Headers.Add("Authorization", $"Bearer {_o.ApiKey}");
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (!json.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0) return null;
            var content = choices[0].GetProperty("message").GetProperty("content").GetString();
            return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
            or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        {
            return null;   // best-effort — caller keeps the raw text
        }
    }
}
```

Register in `DependencyInjection.cs` next to the enricher: `services.AddScoped<Application.Customers.ICompanyBriefPolisher, Harvest.GroqBriefPolisher>();`

- [ ] **Step 4: Run tests** — filter `GroqBriefPolisher` → 3/3 PASS; full `dotnet test` green.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Application/Customers/ICompanyBriefPolisher.cs src/Coldstart.Infrastructure/Harvest/GroqBriefPolisher.cs src/Coldstart.Infrastructure/DependencyInjection.cs tests/Coldstart.Infrastructure.Tests/GroqBriefPolisherTests.cs
git commit -m "feat(infra): GroqBriefPolisher for AI-tightening the company brief"
```

---

### Task 4: `SenderContext` + enricher signature + prompt

**Files:**
- Create: `src/Coldstart.Application/Harvest/SenderContext.cs`
- Modify: `src/Coldstart.Application/Harvest/ILeadEnricher.cs`
- Modify: `src/Coldstart.Infrastructure/Harvest/GroqLeadEnricher.cs`
- Modify: `src/Coldstart.Application/Harvest/HarvestLeads.cs` (pass `SenderContext.None` for now — Task 5 wires the real one)
- Modify: any other `ILeadEnricher` implementation / `EnrichAsync` caller (grep)
- Test: `tests/Coldstart.Infrastructure.Tests/GroqLeadEnricherTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `SenderContext(string? Brief, bool MentionSourceTool)` with `static SenderContext None { get; } = new(null, false)`; `ILeadEnricher.EnrichAsync(RawItem item, NicheOptions niche, SignalResult signal, SenderContext sender, CancellationToken ct)`.

- [ ] **Step 1: Write the failing prompt tests**

Extend `GroqLeadEnricherTests.cs`. The existing `SequenceHandler` returns responses; add a capturing handler to inspect the outgoing prompt:

```csharp
private sealed class CapturingHandler(string body) : HttpMessageHandler
{
    public string? SentBody { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
    {
        SentBody = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

[Fact]
public async Task EnrichAsync_WithBrief_IncludesSenderContextInPrompt()
{
    // Arrange
    var handler = new CapturingHandler(ValidBody);
    var sut = new GroqLeadEnricher(new HttpClient(handler), Opts(), (d, _) => Task.CompletedTask);
    var sender = new SenderContext("Acme helps dentists fill calendars.", MentionSourceTool: false);

    // Act
    await sut.EnrichAsync(Item, Niche, Signal, sender, CancellationToken.None);

    // Assert — the brief is in the prompt; the Coldstart line is NOT (toggle off)
    handler.SentBody.Should().Contain("Acme helps dentists fill calendars.");
    handler.SentBody.Should().NotContain("Coldstart");
}

[Fact]
public async Task EnrichAsync_WithMentionSourceTool_AddsColdstartInstruction()
{
    // Arrange
    var handler = new CapturingHandler(ValidBody);
    var sut = new GroqLeadEnricher(new HttpClient(handler), Opts(), (d, _) => Task.CompletedTask);
    var sender = new SenderContext(Brief: null, MentionSourceTool: true);

    // Act
    await sut.EnrichAsync(Item, Niche, Signal, sender, CancellationToken.None);

    // Assert
    handler.SentBody.Should().Contain("Coldstart");
}
```

Update the FILE's existing `EnrichAsync(...)` calls to pass `SenderContext.None` (they currently call with no sender arg — add it) so they compile.

- [ ] **Step 2: Run to verify failure** — compile error (no `SenderContext`, `EnrichAsync` arity).

- [ ] **Step 3: Implement**

`src/Coldstart.Application/Harvest/SenderContext.cs`:

```csharp
namespace Coldstart.Application.Harvest;

public sealed record SenderContext(string? Brief, bool MentionSourceTool)
{
    public static SenderContext None { get; } = new(null, false);
}
```

`ILeadEnricher.cs`:

```csharp
    Task<EnrichmentResult?> EnrichAsync(RawItem item, NicheOptions niche, SignalResult signal, SenderContext sender, CancellationToken ct);
```

`GroqLeadEnricher.cs`: thread `sender` into `EnrichAsync` → `BuildPrompt(item, niche, signal, sender)`. In `BuildPrompt`, after the `OFFER WE SELL` block add:

```csharp
        {(string.IsNullOrWhiteSpace(sender.Brief) ? "" : $"""

        WHO IS REACHING OUT (the sender's own company — use it for credible context, still open on a specific detail about THEM):
        {sender.Brief}
        """)}
```

and just before the closing `"""`, append the conditional instruction:

```csharp
        {(sender.MentionSourceTool ? """

        Also: mention that you discovered this company using Coldstart — the exact lead-generation tool you are offering them — as concrete proof it works. One natural sentence, not a boast.
        """ : "")}
```

(Keep the existing five-section output contract and banned-phrase rules intact.)

`HarvestLeads.cs`: change the single call to `enricher.EnrichAsync(item, niche, signal, SenderContext.None, ct)` (Task 5 replaces `None`). Grep for any other caller/impl and update.

- [ ] **Step 4: Run tests** — filter `GroqLeadEnricher` → PASS (incl. 2 new); full `dotnet test` green.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Application/Harvest/SenderContext.cs src/Coldstart.Application/Harvest/ILeadEnricher.cs src/Coldstart.Infrastructure/Harvest/GroqLeadEnricher.cs src/Coldstart.Application/Harvest/HarvestLeads.cs tests/Coldstart.Infrastructure.Tests/GroqLeadEnricherTests.cs
git commit -m "feat(harvest): SenderContext threads a sender brief + source-mention flag into the draft prompt"
```

---

### Task 5: Harvester wires the target customer's profile into `SenderContext`

**Files:**
- Modify: `src/Coldstart.Application/Harvest/HarvestLeads.cs`
- Test: `tests/Coldstart.Application.Tests/` (harvest test — mirror the existing `HarvestLeads`/harvest test; if the harvest end-to-end test lives in Infrastructure, add there instead — grep `HarvestLeads`/`IHarvestLeads` in tests)

**Interfaces:**
- Consumes: Task 4 `SenderContext`; `Customer.Profile.Brief`/`MentionSourceTool` (Task 1); the already-resolved `target` customer in `RunAsync`.
- Produces: enrichment called with a `SenderContext` built from the target customer's profile.

- [ ] **Step 1: Write the failing test**

Find the harvest test that uses a fake `ILeadEnricher` (grep `ILeadEnricher` in tests). Add a capturing fake and assert the passed `SenderContext`:

```csharp
[Fact]
public async Task RunAsync_PassesTargetCustomerBriefAndToggle_ToTheEnricher()
{
    // Arrange — seed the target customer WITH a profile brief + toggle via the fake ICustomerRepository
    // (mirror how the existing harvest test seeds sources + target customer);
    // use a fake ILeadEnricher that records the SenderContext it received.

    // Act
    await harvest.RunAsync(CancellationToken.None);

    // Assert
    capturingEnricher.LastSender!.Brief.Should().Be("We help dentists.");
    capturingEnricher.LastSender.MentionSourceTool.Should().BeTrue();
}
```

Write the capturing fake enricher (returns a valid `EnrichmentResult` so a lead is produced, records `SenderContext`). If the existing harvest test already has a fake enricher, extend it to capture the sender.

- [ ] **Step 2: Run to verify failure** — FAIL (sender is `SenderContext.None`, brief null).

- [ ] **Step 3: Implement**

In `HarvestLeads.RunAsync`, after `var target = await ResolveTargetCustomerAsync(ct);`:

```csharp
        var sender = new SenderContext(target.Profile.Brief, target.Profile.MentionSourceTool);
```

and change the enrich call to `await enricher.EnrichAsync(item, niche, signal, sender, ct)`.

- [ ] **Step 4: Run tests** — filter passes; full `dotnet test` green.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Application/Harvest/HarvestLeads.cs tests
git commit -m "feat(harvest): build SenderContext from the target customer's profile"
```

---

### Task 6: Profile UI — brief textarea, toggle, "Polish with AI"

**Files:**
- Modify: `src/Coldstart.Web/Areas/App/Pages/Profile.cshtml.cs`
- Modify: `src/Coldstart.Web/Areas/App/Pages/Profile.cshtml`
- Modify: `src/Coldstart.Web/wwwroot/css/site.css` (small — optional styling)
- Test: `tests/Coldstart.Web.Tests/CustomerZoneTests.cs` (profile brief render + save; polish handler)

**Interfaces:**
- Consumes: `IGetCompanyProfile`/`IUpdateCompanyProfile` (now carrying Brief/MentionSourceTool), `ICompanyBriefPolisher` (Task 3).
- Produces: profile page fields `Input.Brief`, `Input.MentionSourceTool`; `OnPostPolishAsync` handler.

- [ ] **Step 1: Write the failing tests**

Read `CustomerZoneTests.cs` for the profile get/post pattern (`ProfilePost_PersistsAndRendersOnZone`) + antiforgery helper. Add:

```csharp
[Fact]
public async Task ProfilePost_PersistsBriefAndToggle()
{
    // Arrange — signed-in customer client (mirror ProfilePost_PersistsAndRendersOnZone)
    // Act — POST /app/profile with Input.Brief + Input.MentionSourceTool=true + other fields
    // (scrape antiforgery from GET /app/profile)
    // Assert — GET /app/profile shows the brief text and the checkbox checked
    var page = await client.GetStringAsync("/app/profile");
    page.Should().Contain("We help dentists fill calendars.");
}

[Fact]
public async Task ProfilePolish_ReturnsPolishedText_WithoutPersisting()
{
    // Arrange — register a fake ICompanyBriefPolisher in the factory returning "POLISHED BRIEF";
    //           signed-in client; scrape token from GET /app/profile
    // Act — POST /app/profile?handler=Polish with Input.Brief="rough notes"
    // Assert — response (200) contains "POLISHED BRIEF"; and a fresh GET shows the STORED brief unchanged (not "POLISHED BRIEF"), proving polish didn't save
}
```

Register a `FakeCompanyBriefPolisher : ICompanyBriefPolisher` in `ColdstartWebFactory` (mirror `FakeEmailSender`/`FakeMailbox`): a settable return value; default returns its input prefixed or a fixed string. For `ProfilePolish...` set it to return `"POLISHED BRIEF"`.

- [ ] **Step 2: Run to verify failure** — FAIL (no brief field / no Polish handler).

- [ ] **Step 3: Implement the page model**

In `Profile.cshtml.cs`: add `ICompanyBriefPolisher polisher` to the primary constructor; extend `ProfileInput` with `public string? Brief { get; set; }` and `public bool MentionSourceTool { get; set; }`; map them in `OnGetAsync` (from the view) and `OnPostAsync` (into `CompanyProfileInput`); add:

```csharp
    public async Task<IActionResult> OnPostPolishAsync(CancellationToken ct)
    {
        var polished = string.IsNullOrWhiteSpace(Input.Brief) ? null : await polisher.PolishAsync(Input.Brief!, ct);
        if (polished is not null) Input.Brief = polished;
        else StatusMessage = "Couldn't polish just now — your text is unchanged.";
        return Page();   // re-render with the polished (unsaved) text; admin reviews then Saves
    }
```

Update `OnGetAsync`'s `Input = new ProfileInput { ... }` to include `Brief = v.Brief, MentionSourceTool = v.MentionSourceTool`, and `OnPostAsync`'s `CompanyProfileInput(...)` to pass `Input.Brief, Input.MentionSourceTool`.

- [ ] **Step 4: Add the UI**

In `Profile.cshtml`, inside the profile form (before the submit), add:

```html
  <div class="field">
    <label asp-for="Input.Brief">Company brief <span class="field-hint">— a short "who we are" used as context in your outreach</span></label>
    <textarea asp-for="Input.Brief" rows="4" placeholder="e.g. Acme helps dental clinics fill last-minute cancellations with automated waitlist texts."></textarea>
    <button class="btn btn-ghost btn-sm" type="submit" asp-page-handler="Polish">Polish with AI</button>
  </div>
  <div class="field field-check">
    <label><input asp-for="Input.MentionSourceTool" type="checkbox"> Mention leads were found via Coldstart</label>
  </div>
```

(The Polish button is a submit posting to the `Polish` handler; the main Save submit stays as-is. `@Model.StatusMessage` is already rendered on the page — if not, add `@if (Model.StatusMessage is not null) { <p class="form-status">@Model.StatusMessage</p> }` near the top of the form.)

- [ ] **Step 5: Run tests** — filter `CustomerZone` → PASS; full `dotnet test` green.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Web/Areas/App/Pages/Profile.cshtml.cs src/Coldstart.Web/Areas/App/Pages/Profile.cshtml src/Coldstart.Web/wwwroot/css/site.css tests/Coldstart.Web.Tests/CustomerZoneTests.cs tests/Coldstart.Web.Tests/ColdstartWebFactory.cs
git commit -m "feat(app): company brief textarea, AI polish button, and source-mention toggle on the profile page"
```

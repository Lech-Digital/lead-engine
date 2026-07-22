# Lead Review Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rework the customer-zone review surface per `docs/superpowers/specs/2026-07-22-lead-review-redesign-design.md`: compact list rows, a two-column detail view with status-driven actions and a from-your-own-domain send handoff, a per-lead notes collection, and the "Mark as sent" fix.

**Architecture:** Bottom-up — a `LeadNote` owned collection on the `Lead` aggregate (Domain) → EF owned-collection mapping + migration (Infrastructure) → `LeadDetail` read-model fields + note CRUD on `ILeadWorkflow` through the existing tenant choke point (Application) → compact list rows and the two-column detail view with client-side send handoffs (Web). No new sending; the handoff opens the customer's own mail client.

**Tech Stack:** .NET 10 Razor Pages, EF Core + Postgres, vanilla JS (no deps), xUnit + FluentAssertions, WebApplicationFactory + Testcontainers Postgres.

## Global Constraints

- Hexagonal (CS-HEX): Domain has no EF/ASP.NET; ports/DTOs in Application; `LeadNote` is part of the `Lead` aggregate (owned, no separate repository). `internal` for impl classes, `public record` for DTOs.
- Async all the way, `CancellationToken` down to IO (CS-DATA-03). No `IQueryable` crossing to Application.
- Time and ids are passed IN (the codebase's pattern): the Web layer generates note `Guid`s and supplies `DateTimeOffset.UtcNow`; the domain never reads a clock.
- MUST NOT change: `?tab=new|accepted|discarded` slugs, `data-tab`/`data-bucket`/`data-lead-id` values, the `?handler=Move` endpoint + `app-leads.js` move contract, page handler names for existing actions (Save/Accept/Discard/Reopen/MarkSent), `[Authorize]` + antiforgery on all POSTs.
- Send handoff is client-side only (no new endpoint, no server send). Gmail URL `https://mail.google.com/mail/?view=cm&fs=1&to={to}&su={subject}&body={body}` and `mailto:{to}?subject={subject}&body={body}`, all `encodeURIComponent`-encoded; Copy uses `navigator.clipboard`.
- "Mark as sent" renders/enables ONLY on an Approved lead with a recipient present (fixes the bug where it showed on every unsent lead).
- Tests xUnit + FluentAssertions, `MethodName_StateUnderTest_ExpectedBehavior`, AAA comments. Full-solution `dotnet test` green at the end of every task (report the real command + per-project totals). Docker running for the Postgres Testcontainer.
- Commits conventional, NO Co-Authored-By trailers. Stage only each task's listed paths — the repo has unrelated uncommitted user work; never stage it.

---

### Task 1: Domain — `LeadNote` owned collection on `Lead`

**Files:**
- Create: `src/Coldstart.Domain/LeadNote.cs`
- Modify: `src/Coldstart.Domain/Lead.cs`
- Test: `tests/Coldstart.Domain.Tests/LeadNoteTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `LeadNote` (Id/Text/CreatedAt/UpdatedAt); on `Lead`: `IReadOnlyList<LeadNote> Notes`, `Guid AddNote(string text, DateTimeOffset when)` (returns the new id), `void UpdateNote(Guid noteId, string text, DateTimeOffset when)`, `void RemoveNote(Guid noteId)`. `AddNote` also accepts a caller-supplied id overload `AddNote(Guid id, string text, DateTimeOffset when)` (Web supplies the id).

- [ ] **Step 1: Write the failing tests**

Create `tests/Coldstart.Domain.Tests/LeadNoteTests.cs`:

```csharp
using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class LeadNoteTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);

    private static Lead NewLead() =>
        Lead.Create(CustomerId.New(), "Acme Co", Fit.Yes, "Subj", "Body", T0);

    [Fact]
    public void AddNote_AppendsNote_AndReturnsId()
    {
        // Arrange
        var lead = NewLead();

        // Act
        var id = lead.AddNote("called them", T0);

        // Assert
        lead.Notes.Should().ContainSingle();
        lead.Notes[0].Id.Should().Be(id);
        lead.Notes[0].Text.Should().Be("called them");
        lead.Notes[0].CreatedAt.Should().Be(T0);
        lead.Notes[0].UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void AddNote_WithBlankText_Throws()
    {
        // Arrange
        var lead = NewLead();

        // Act
        var act = () => lead.AddNote("   ", T0);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void UpdateNote_ChangesTextAndStampsUpdatedAt()
    {
        // Arrange
        var lead = NewLead();
        var id = lead.AddNote("first", T0);

        // Act
        lead.UpdateNote(id, "revised", T1);

        // Assert
        lead.Notes[0].Text.Should().Be("revised");
        lead.Notes[0].UpdatedAt.Should().Be(T1);
    }

    [Fact]
    public void UpdateNote_WithUnknownId_IsNoOp()
    {
        // Arrange
        var lead = NewLead();
        lead.AddNote("keep", T0);

        // Act
        var act = () => lead.UpdateNote(Guid.NewGuid(), "x", T1);

        // Assert
        act.Should().NotThrow();
        lead.Notes.Should().ContainSingle().Which.Text.Should().Be("keep");
    }

    [Fact]
    public void RemoveNote_RemovesById_UnknownIdIsNoOp()
    {
        // Arrange
        var lead = NewLead();
        var id = lead.AddNote("bye", T0);

        // Act
        lead.RemoveNote(Guid.NewGuid());   // no-op
        lead.RemoveNote(id);               // removes

        // Assert
        lead.Notes.Should().BeEmpty();
    }

    [Fact]
    public void AddNote_IsAllowedOnASentLead()
    {
        // Arrange — get the lead to Sent
        var lead = NewLead();
        lead.Approve(new EmailAddress("x@y.com"));
        lead.MarkSent(T0);

        // Act
        var act = () => lead.AddNote("post-send follow-up", T1);

        // Assert
        act.Should().NotThrow();
        lead.Notes.Should().ContainSingle();
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Coldstart.Domain.Tests --filter "FullyQualifiedName~LeadNote"`
Expected: compile error — `LeadNote`/`AddNote` don't exist.

- [ ] **Step 3: Implement `LeadNote` and the `Lead` methods**

Create `src/Coldstart.Domain/LeadNote.cs`:

```csharp
namespace Coldstart.Domain;

public sealed class LeadNote
{
    public Guid Id { get; private set; }
    public string Text { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private LeadNote() { }   // EF materialization

    internal LeadNote(Guid id, string text, DateTimeOffset when)
    {
        Id = id;
        Text = text;
        CreatedAt = when;
    }

    internal void Update(string text, DateTimeOffset when)
    {
        Text = text;
        UpdatedAt = when;
    }
}
```

In `src/Coldstart.Domain/Lead.cs`, add the backing field, the exposed collection, and the three methods (place after `MatchedSignals` and before `private Lead()`):

```csharp
    private readonly List<LeadNote> _notes = [];
    public IReadOnlyList<LeadNote> Notes => _notes;
```

and (place after `MarkSent`):

```csharp
    public Guid AddNote(string text, DateTimeOffset when) => AddNote(Guid.NewGuid(), text, when);

    public Guid AddNote(Guid id, string text, DateTimeOffset when)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException("Note text is required.");
        _notes.Add(new LeadNote(id, text.Trim(), when));
        return id;
    }

    public void UpdateNote(Guid noteId, string text, DateTimeOffset when)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException("Note text is required.");
        _notes.FirstOrDefault(n => n.Id == noteId)?.Update(text.Trim(), when);
    }

    public void RemoveNote(Guid noteId) => _notes.RemoveAll(n => n.Id == noteId);
```

Note: `_notes` is a `readonly List` initialized inline; EF populates owned collections via the backing field (configured in Task 2). The `private Lead()` ctor stays; EF sets the field.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/Coldstart.Domain.Tests --filter "FullyQualifiedName~LeadNote"` → 6/6 PASS. Then full `dotnet test` → green.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Domain/LeadNote.cs src/Coldstart.Domain/Lead.cs tests/Coldstart.Domain.Tests/LeadNoteTests.cs
git commit -m "feat(domain): lead notes as an owned collection on the Lead aggregate"
```

---

### Task 2: EF mapping + migration for `lead_notes`

**Files:**
- Modify: `src/Coldstart.Infrastructure/Configurations/LeadConfiguration.cs`
- Create: `src/Coldstart.Infrastructure/Migrations/<timestamp>_AddLeadNotes.cs` (+ `.Designer.cs`, generated)
- Modify: `src/Coldstart.Infrastructure/Migrations/ColdstartDbContextModelSnapshot.cs` (tool-regenerated)
- Test: `tests/Coldstart.Infrastructure.Tests/LeadRepositoryTests.cs` (add a note round-trip test)

**Interfaces:**
- Consumes: Task 1 `Lead.Notes`/`AddNote`.
- Produces: a persisted `lead_notes` table; `ILeadRepository.GetAsync` returns a lead with its `Notes` populated (owned collections auto-load).

- [ ] **Step 1: Write the failing repository test**

Read `tests/Coldstart.Infrastructure.Tests/LeadRepositoryTests.cs` first and mirror its existing add/get pattern (it uses the `PostgresFixture`). Add:

```csharp
[Fact]
public async Task GetAsync_AfterAddingNotes_RoundTripsThem()
{
    // Arrange
    await using var ctx = _fixture.NewContext();
    var repo = new LeadRepository(ctx);
    var lead = Lead.Create(new CustomerId(Guid.NewGuid()), "Acme Co", Fit.Yes, "S", "D", DateTimeOffset.UtcNow);
    lead.AddNote("first note", DateTimeOffset.UtcNow);
    lead.AddNote("second note", DateTimeOffset.UtcNow);
    await repo.AddAsync(lead, CancellationToken.None);

    // Act — fresh context to force a real DB read
    await using var ctx2 = _fixture.NewContext();
    var reloaded = await new LeadRepository(ctx2).GetAsync(lead.Id, CancellationToken.None);

    // Assert
    reloaded!.Notes.Should().HaveCount(2);
    reloaded.Notes.Select(n => n.Text).Should().Contain(["first note", "second note"]);
}
```

(Match the fixture/context-creation helpers actually used in that file — `_fixture.NewContext()`/`LeadRepository` names may differ; copy the file's real pattern. If `AddAsync` doesn't `SaveChanges`, follow whatever the existing tests do to persist.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~LeadRepository"`
Expected: FAIL — notes not persisted (no table/mapping); reloaded.Notes empty or a model error.

- [ ] **Step 3: Map the owned collection**

In `LeadConfiguration.Configure`, before the closing brace, add:

```csharp
        builder.OwnsMany(l => l.Notes, n =>
        {
            n.ToTable("lead_notes");
            n.WithOwner().HasForeignKey("LeadId");
            n.HasKey("Id");
            n.Property(x => x.Id).ValueGeneratedNever();
            n.Property(x => x.Text).IsRequired();
            n.Property(x => x.CreatedAt);
            n.Property(x => x.UpdatedAt);
        });
        builder.Navigation(l => l.Notes).UsePropertyAccessMode(PropertyAccessMode.Field);
```

Add `using Microsoft.EntityFrameworkCore.Metadata;` if `PropertyAccessMode` isn't resolved.

- [ ] **Step 4: Generate the migration**

The EF design-time package doesn't flow to the Web startup project, so add it temporarily (revert after):

```bash
dotnet add src/Coldstart.Web package Microsoft.EntityFrameworkCore.Design
dotnet ef migrations add AddLeadNotes \
  --project src/Coldstart.Infrastructure --startup-project src/Coldstart.Web --output-dir Migrations
```

Then open the generated `Up` — it should `CreateTable("lead_notes")` with columns `Id` (PK), `LeadId` (FK → leads, cascade delete), `Text`, `CreatedAt`, `UpdatedAt` (nullable). Revert the Web csproj change:

```bash
git checkout src/Coldstart.Web/Coldstart.Web.csproj
```

Confirm `git status` shows only the two new migration files + regenerated snapshot (no csproj change). Never hand-edit the snapshot.

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~LeadRepository"` → PASS (the Testcontainer applies the new migration on boot). Then full `dotnet test` → green.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Infrastructure/Configurations/LeadConfiguration.cs src/Coldstart.Infrastructure/Migrations tests/Coldstart.Infrastructure.Tests/LeadRepositoryTests.cs
git commit -m "feat(infra): persist lead notes as an owned collection (lead_notes table)"
```

---

### Task 3: Application — read model fields + note CRUD on the workflow

**Files:**
- Modify: `src/Coldstart.Application/Leads/LeadDetail.cs`
- Create: `src/Coldstart.Application/Leads/LeadNoteView.cs`
- Modify: `src/Coldstart.Application/Leads/GetLeadDetail.cs`
- Modify: `src/Coldstart.Application/Leads/ILeadWorkflow.cs`
- Modify: `src/Coldstart.Application/Leads/LeadWorkflow.cs`
- Test: `tests/Coldstart.Application.Tests/LeadWorkflowTests.cs` (or the file that tests the workflow — mirror it)

**Interfaces:**
- Consumes: Task 1 `Lead.AddNote/UpdateNote/RemoveNote/Notes`.
- Produces: `LeadDetail` gains `string MatchedSignals`, `string EmailConfidence`, `IReadOnlyList<LeadNoteView> Notes` (appended last, positional record); `LeadNoteView(Guid Id, string Text, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt)`; `ILeadWorkflow.AddNoteAsync(Guid customerId, Guid leadId, Guid noteId, string text, CancellationToken ct)`, `UpdateNoteAsync(Guid customerId, Guid leadId, Guid noteId, string text, CancellationToken ct)`, `RemoveNoteAsync(Guid customerId, Guid leadId, Guid noteId, CancellationToken ct)`.

- [ ] **Step 1: Write the failing tests**

Read the existing workflow test file (likely `tests/Coldstart.Application.Tests/LeadWorkflowTests.cs`) and mirror its fake-repository + AAA pattern. Add:

```csharp
[Fact]
public async Task AddNoteAsync_ForOwnedLead_PersistsNote()
{
    // Arrange — mirror the file's fake ILeadRepository seeding a lead owned by `customerId`
    // (copy the existing helper that creates a lead + workflow)

    // Act
    var noteId = Guid.NewGuid();
    var result = await workflow.AddNoteAsync(customerId, leadId, noteId, "ring them Tuesday", CancellationToken.None);

    // Assert
    result.IsOk.Should().BeTrue();
    // reload via the fake repo and assert the lead has the note
    (await repo.GetAsync(new LeadId(leadId), CancellationToken.None))!.Notes
        .Should().ContainSingle(n => n.Id == noteId && n.Text == "ring them Tuesday");
}

[Fact]
public async Task AddNoteAsync_WithBlankText_ReturnsInvalid()
{
    // Arrange (same seeding)
    // Act
    var result = await workflow.AddNoteAsync(customerId, leadId, Guid.NewGuid(), "   ", CancellationToken.None);
    // Assert
    result.Status.Should().Be(LeadActionStatus.Invalid);
}

[Fact]
public async Task AddNoteAsync_ForAnotherCustomersLead_ReturnsNotFound()
{
    // Arrange (lead owned by customerId)
    // Act — a different customer id
    var result = await workflow.AddNoteAsync(Guid.NewGuid(), leadId, Guid.NewGuid(), "x", CancellationToken.None);
    // Assert
    result.Status.Should().Be(LeadActionStatus.NotFound);
}

[Fact]
public async Task RemoveNoteAsync_RemovesTheNote()
{
    // Arrange — add a note first
    var noteId = Guid.NewGuid();
    await workflow.AddNoteAsync(customerId, leadId, noteId, "temp", CancellationToken.None);
    // Act
    var result = await workflow.RemoveNoteAsync(customerId, leadId, noteId, CancellationToken.None);
    // Assert
    result.IsOk.Should().BeTrue();
    (await repo.GetAsync(new LeadId(leadId), CancellationToken.None))!.Notes.Should().BeEmpty();
}
```

Also add one for `GetLeadDetail` including notes if that file has a test class; otherwise the web test in Task 6 covers it.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Coldstart.Application.Tests --filter "FullyQualifiedName~LeadWorkflow"`
Expected: compile error — `AddNoteAsync` etc. don't exist.

- [ ] **Step 3: Implement**

`src/Coldstart.Application/Leads/LeadNoteView.cs`:

```csharp
namespace Coldstart.Application.Leads;

public sealed record LeadNoteView(Guid Id, string Text, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
```

`LeadDetail.cs` — append three fields (positional record; keep existing order, add at the end):

```csharp
public sealed record LeadDetail(
    Guid Id,
    string Company,
    string Fit,
    string Status,
    string Subject,
    string Draft,
    string? RecipientEmail,
    string Source,
    string SourceUrl,
    double Score,
    DateTimeOffset? SentAt,
    IReadOnlyList<ContactLink> ContactLinks,
    string MatchedSignals,
    string EmailConfidence,
    IReadOnlyList<LeadNoteView> Notes);
```

`GetLeadDetail.cs` — map the new fields:

```csharp
        return new LeadDetail(
            lead.Id.Value, lead.Company, lead.Fit.ToString(), lead.Status.ToString(),
            lead.Subject, lead.Draft, lead.RecipientEmail?.Value,
            lead.Source, lead.SourceUrl, lead.Score, lead.SentAt,
            ContactLinkBuilder.Build(lead.Company, lead.SourceUrl),
            lead.MatchedSignals,
            lead.EmailConfidence.ToString(),
            lead.Notes.Select(n => new LeadNoteView(n.Id, n.Text, n.CreatedAt, n.UpdatedAt)).ToList());
```

`ILeadWorkflow.cs` — add:

```csharp
    Task<LeadActionResult> AddNoteAsync(Guid customerId, Guid leadId, Guid noteId, string text, CancellationToken ct);
    Task<LeadActionResult> UpdateNoteAsync(Guid customerId, Guid leadId, Guid noteId, string text, CancellationToken ct);
    Task<LeadActionResult> RemoveNoteAsync(Guid customerId, Guid leadId, Guid noteId, CancellationToken ct);
```

`LeadWorkflow.cs` — implement via the existing `MutateAsync` choke point (note text validation is in the domain, which throws `DomainException` → `MutateAsync` already converts that to `Invalid`):

```csharp
    public Task<LeadActionResult> AddNoteAsync(Guid customerId, Guid leadId, Guid noteId, string text, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead => { lead.AddNote(noteId, text, DateTimeOffset.UtcNow); return LeadActionResult.Ok; });

    public Task<LeadActionResult> UpdateNoteAsync(Guid customerId, Guid leadId, Guid noteId, string text, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead => { lead.UpdateNote(noteId, text, DateTimeOffset.UtcNow); return LeadActionResult.Ok; });

    public Task<LeadActionResult> RemoveNoteAsync(Guid customerId, Guid leadId, Guid noteId, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead => { lead.RemoveNote(noteId); return LeadActionResult.Ok; });
```

Check any other `ILeadWorkflow` implementations (e.g. a test fake) and add the three methods there too — `grep -rn "ILeadWorkflow" src tests`.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/Coldstart.Application.Tests --filter "FullyQualifiedName~LeadWorkflow"` → PASS. Full `dotnet test` → green (the `LeadDetail` positional change may require updating any direct constructions in tests — grep `new LeadDetail(` and fix).

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Application/Leads tests/Coldstart.Application.Tests
git commit -m "feat(app): lead notes CRUD on the workflow; notes+signals in LeadDetail"
```

---

### Task 4: Compact list rows on `/app`

**Files:**
- Modify: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml`
- Modify: `src/Coldstart.Web/wwwroot/css/site.css`
- Test: `tests/Coldstart.Web.Tests/LeadReviewTests.cs`

**Interfaces:**
- Consumes: `LeadSummary` (already has Company/Fit/Subject/Source/Score/CreatedAt/MatchedSignals).
- Produces: compact `.lead-row` markup keeping `data-lead-id`/`data-bucket`/draggable + hover Approve/Skip.

- [ ] **Step 1: Update the failing tests**

The current `LeadReviewTests` assert tab labels/counts (unaffected) and lead company names appear (still true). Add a row-shape test and keep the rest:

```csharp
[Fact]
public async Task Index_NewTab_RendersCompactRow_WithWhyNowAndSubject()
{
    // Arrange — seed a lead with MatchedSignals + subject (mirror AddLeadForUserAsync;
    // if it doesn't set MatchedSignals, extend the helper or seed via the repo)

    // Act
    var html = await client.GetStringAsync("/app?tab=new");

    // Assert — compact row shows company, subject, and the why-now signal
    html.Should().Contain("lead-row");
    html.Should().Contain("Pending Co");
    html.Should().Contain("data-lead-id");
}
```

If any existing assertion pins the old `app-lead` card class specifically as structure (not just presence of a company name), update it to `lead-row`. Run the file first to see which break.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~LeadReview"`
Expected: the new test FAILs (no `lead-row`); note any existing failures from renamed classes.

- [ ] **Step 3: Rewrite the card helper as a compact row**

In `Index.cshtml`, replace the `Card` local function with a compact row (keep the `Bucket` function and its `data-bucket`/`data-count` wrapper unchanged; keep `draggable` and `data-lead-id`):

```html
  @{
    void Card(Coldstart.Application.Leads.LeadSummary lead, bool draggable, bool actionable)
    {
      <article class="lead-row" data-lead-id="@lead.Id" draggable="@(draggable ? "true" : "false")">
        <a class="lead-row-main" href="/app/leads/@lead.Id">
          <div class="lead-row-head">
            <span class="lead-row-co">@lead.Company</span>
            <span class="fit fit-@lead.Fit.ToLower()">@lead.Fit</span>
            <span class="ts lead-row-ts">@lead.CreatedAt.ToString("dd MMM · HH:mm", System.Globalization.CultureInfo.InvariantCulture)</span>
          </div>
          <div class="lead-row-sub">
            @if (!string.IsNullOrWhiteSpace(lead.MatchedSignals)) { <span class="lead-row-why">⚡ @lead.MatchedSignals.Split(',')[0].Trim()</span><span class="lead-row-dot">·</span> }
            <span class="lead-row-subj">@lead.Subject</span>
          </div>
        </a>
        @if (actionable)
        {
          <div class="lead-row-actions">
            <button type="button" class="btn btn-ghost btn-sm app-act-skip" data-move="discarded">Skip</button>
            <button type="button" class="btn btn-primary btn-sm app-act-approve" data-move="accepted">Approve</button>
          </div>
        }
      </article>
    }
  }
```

(The `actionable` flag is already threaded through `Bucket`/`Card` from the redesign — `true` only for `Model.New`.)

- [ ] **Step 4: Add compact-row CSS**

Append to `site.css` (near the existing `.app-lead` rules; you may leave the old `.app-lead*` rules — they're now unused but harmless, or delete them if grep shows no other user):

```css
/* ===== Compact lead rows ===== */
.app-leads { display: flex; flex-direction: column; gap: 8px; }
.lead-row { display: flex; align-items: center; justify-content: space-between; gap: 12px; background: #fff; border: 1px solid var(--line-soft); border-radius: 10px; padding: 10px 14px; }
.lead-row:hover { border-color: var(--line-2, #D8C5A4); }
.lead-row-main { flex: 1; min-width: 0; text-decoration: none; color: inherit; display: block; }
.lead-row-head { display: flex; align-items: baseline; gap: 10px; }
.lead-row-co { font-family: var(--display); font-weight: 800; font-size: 0.98rem; color: var(--ink); }
.lead-row-ts { margin-left: auto; font-size: 0.72rem; color: var(--body-subtle); }
.lead-row-sub { display: flex; align-items: baseline; gap: 8px; margin-top: 2px; overflow: hidden; }
.lead-row-why { color: #B3671A; font-size: 0.82rem; font-weight: 600; white-space: nowrap; }
.lead-row-dot { color: var(--body-subtle); }
.lead-row-subj { color: var(--body-muted); font-size: 0.86rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.lead-row-actions { display: flex; gap: 6px; opacity: 0; transition: opacity .12s ease; flex-shrink: 0; }
.lead-row:hover .lead-row-actions, .lead-row:focus-within .lead-row-actions { opacity: 1; }
@media (hover: none) { .lead-row-actions { opacity: 1; } }
/* ===== end compact lead rows ===== */
```

- [ ] **Step 5: Confirm the move JS still binds**

`app-leads.js` binds Approve/Skip via `.app-lead-actions button[data-move]` and drag via `.app-lead[draggable]`/`data-lead-id`. The row uses `.lead-row-actions` and `.lead-row`. Update the two selectors in `app-leads.js`: `.app-lead-actions button[data-move]` → `.lead-row-actions button[data-move]`, and `.app-lead[draggable="true"]` → `.lead-row[draggable="true"]`. Leave `requestMove`/`moveCardTo`/`data-lead-id`/`data-bucket` logic untouched. (Grep `app-lead` in app-leads.js and update only the row/action selectors, not the bucket ones.)

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~LeadReview|FullyQualifiedName~CustomerZone"` → PASS. Full `dotnet test` → green.

- [ ] **Step 7: Commit**

```bash
git add src/Coldstart.Web/Areas/App/Pages/Index.cshtml src/Coldstart.Web/wwwroot/css/site.css src/Coldstart.Web/wwwroot/js/app-leads.js tests/Coldstart.Web.Tests/LeadReviewTests.cs
git commit -m "feat(app): compact lead rows with hover Approve/Skip"
```

---

### Task 5: Two-column detail view — structure, status-driven actions, Mark-sent fix, send handoff

**Files:**
- Rewrite: `src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml`
- Create: `src/Coldstart.Web/wwwroot/js/lead-detail.js`
- Modify: `src/Coldstart.Web/wwwroot/css/site.css`
- Test: `tests/Coldstart.Web.Tests/LeadDetailTests.cs`

**Interfaces:**
- Consumes: `LeadDetail` (now with `MatchedSignals`/`EmailConfidence`/`Notes`), existing `DetailModel` handlers (Save/Accept/Discard/Reopen/MarkSent). Notes UI is added in Task 6 — this task renders the detail shell, email column, context column (without the notes block), and the handoff.
- Produces: two-column detail markup; `#lead-recipient`/`#lead-subject`/`#lead-body` field ids the handoff JS reads; handoff buttons with ids `#handoff-gmail`/`#handoff-mailto`/`#handoff-copy`.

- [ ] **Step 1: Update the failing tests**

`LeadDetailTests` currently asserts `Contain("Acme Co")` and `Contain("linkedin.com/search")` — both still true. Add:

```csharp
[Fact]
public async Task Detail_ForPendingLead_ShowsApprove_AndHidesMarkSent()
{
    // Arrange — seed a PendingReview lead for the user (mirror the file's helper)
    // Act
    var html = await client.GetStringAsync($"/app/leads/{id}");
    // Assert
    html.Should().Contain("handler=Accept");        // Approve present
    html.Should().NotContain("handler=MarkSent");    // mark-sent NOT offered on a pending lead
}

[Fact]
public async Task Detail_ForApprovedLeadWithRecipient_ShowsMarkSentAndHandoff()
{
    // Arrange — seed an Approved lead WITH a recipient (approve via workflow or repo)
    // Act
    var html = await client.GetStringAsync($"/app/leads/{id}");
    // Assert
    html.Should().Contain("handler=MarkSent");
    html.Should().Contain("handoff-gmail");          // send handoff rendered
    html.Should().Contain("Send from your own inbox");
}
```

Adapt the "approved with recipient" seeding to the file's helpers (approve through `ILeadWorkflow.AcceptAsync` with a recipient, or set status+recipient via the repo).

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~LeadDetail"`
Expected: new tests FAIL (old view shows MarkSent on all non-sent leads; no handoff markup).

- [ ] **Step 3: Rewrite `Detail.cshtml`**

Full replacement (two columns; status-driven; the Notes block placeholder comment is filled in Task 6):

```html
@page "/app/leads/{id:guid}"
@model Coldstart.Web.Areas.App.Pages.Leads.DetailModel
@{
    ViewData["Title"] = "Lead";
    Layout = "/Pages/Shared/_Layout.cshtml";
    var lead = Model.Lead;
    var status = lead.Status;
    var pending = status == "PendingReview";
    var approved = status == "Approved";
    var sent = status == "Sent";
    var skipped = status is "Shelved" or "Rejected";
    var hasRecipient = !string.IsNullOrWhiteSpace(lead.RecipientEmail);
}

<header class="app-bar">
  <a class="wordmark" href="/">coldstart<i>*</i></a>
  <nav class="app-nav"><a href="/app">Your briefing</a></nav>
</header>

<main class="wrap app-zone lead-detail">
  <p class="auth-alt"><a href="/app">← Back to your briefing</a></p>

  <div class="ld-head">
    <div><h1 class="ld-co">@lead.Company</h1> <span class="fit fit-@lead.Fit.ToLower()">@lead.Fit</span></div>
    <div class="ld-status ts">
      @(pending ? "To review" : approved ? "Approved" : sent ? $"Sent{(lead.SentAt is null ? "" : " on " + lead.SentAt.Value.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture))}" : "Skipped")
    </div>
  </div>

  <div asp-validation-summary="All" class="field-error"></div>
  @if (Model.Notice is not null) { <p class="mail-flash">@Model.Notice</p> }

  <div class="ld-grid">
    <!-- LEFT: the email -->
    <div class="ld-col">
      <section class="panel">
        <h4>The email</h4>
        <form method="post">
          <div class="field"><label asp-for="Input.Recipient">Recipient</label>
            <input asp-for="Input.Recipient" id="lead-recipient" type="email" placeholder="name@company.com — set before sending" disabled="@sent"></div>
          <div class="field"><label asp-for="Input.Subject">Subject</label>
            <input asp-for="Input.Subject" id="lead-subject" disabled="@sent"></div>
          <div class="field"><label asp-for="Input.Draft">Body</label>
            <textarea asp-for="Input.Draft" id="lead-body" rows="8" disabled="@sent"></textarea></div>

          @if (!sent)
          {
            <div class="ld-actions">
              <button class="btn btn-ghost" type="submit" asp-page-handler="Save">Save draft</button>
              @if (pending || skipped) { <button class="btn btn-primary" type="submit" asp-page-handler="Accept">Approve →</button> }
              @if (approved) { <button class="btn btn-ghost" type="submit" asp-page-handler="Reopen">Move back to review</button> }
              @if (!skipped) { <button class="btn btn-ghost" type="submit" asp-page-handler="Discard">Skip</button> }
            </div>
          }
          else { <p class="app-sub">This lead was sent and is read-only.</p> }
        </form>
      </section>

      @if (approved)
      {
        <section class="panel ld-send">
          <h4>Send from your own inbox</h4>
          <p class="ld-send-note">It goes out from <b>your</b> address, not ours. Open your mail with the draft ready, then mark it sent.</p>
          <div class="ld-send-btns">
            <a class="btn btn-ghost" id="handoff-gmail" target="_blank" rel="noopener">✉ Open in Gmail</a>
            <a class="btn btn-ghost" id="handoff-mailto">✉ Open in email app</a>
            <button class="btn btn-ghost" id="handoff-copy" type="button">⧉ Copy email</button>
            <span class="ld-copied ts" id="handoff-copied" hidden>Copied ✓</span>
          </div>
          <form method="post" class="ld-marksent">
            <button class="btn btn-primary" type="submit" asp-page-handler="MarkSent" disabled="@(!hasRecipient)">Mark as sent ✓</button>
            @if (!hasRecipient) { <span class="ts">Add a recipient first.</span> }
          </form>
        </section>
      }
    </div>

    <!-- RIGHT: context (+ notes added in Task 6) -->
    <div class="ld-col">
      <section class="panel">
        <h4>Why this lead</h4>
        @if (!string.IsNullOrWhiteSpace(lead.MatchedSignals))
        {
          <p class="ld-why">⚡ Why now</p>
          <p class="ld-why-text">@lead.MatchedSignals.Split(',')[0].Trim()</p>
        }
        <dl class="ld-facts">
          <div><dt>Signals</dt><dd class="ts">@lead.MatchedSignals</dd></div>
          <div><dt>Source</dt><dd>@lead.Source @if (!string.IsNullOrEmpty(lead.SourceUrl)) { <a class="lnk" href="@lead.SourceUrl" target="_blank" rel="noopener">→</a> }</dd></div>
          <div><dt>Score</dt><dd>@lead.Score.ToString("0.00") · @lead.EmailConfidence.ToLower()</dd></div>
        </dl>
      </section>

      @if (lead.ContactLinks.Count > 0)
      {
        <section class="panel">
          <h4>Find the decision-maker</h4>
          <ul class="ld-links">@foreach (var link in lead.ContactLinks) { <li><a class="lnk" href="@link.Url" target="_blank" rel="noopener">@link.Label</a></li> }</ul>
        </section>
      }

      <!-- NOTES-PANEL: added in Task 6 -->
    </div>
  </div>
</main>
<script src="~/js/lead-detail.js" defer></script>
```

- [ ] **Step 4: Create the handoff JS**

`src/Coldstart.Web/wwwroot/js/lead-detail.js`:

```js
// Send handoff: build Gmail-compose / mailto / clipboard from the current field values,
// so the email is sent from the customer's OWN mail client (their domain), not ours.
(function () {
  "use strict";
  var to = document.getElementById("lead-recipient");
  var subj = document.getElementById("lead-subject");
  var body = document.getElementById("lead-body");
  var gmail = document.getElementById("handoff-gmail");
  var mailto = document.getElementById("handoff-mailto");
  var copy = document.getElementById("handoff-copy");
  var copied = document.getElementById("handoff-copied");
  if (!subj || !body) return;   // not on an approved lead

  function vals() {
    return {
      to: (to && to.value ? to.value : "").trim(),
      su: subj.value || "",
      bd: body.value || ""
    };
  }
  function refresh() {
    var v = vals();
    if (gmail) gmail.href = "https://mail.google.com/mail/?view=cm&fs=1&to=" +
      encodeURIComponent(v.to) + "&su=" + encodeURIComponent(v.su) + "&body=" + encodeURIComponent(v.bd);
    if (mailto) mailto.href = "mailto:" + encodeURIComponent(v.to) +
      "?subject=" + encodeURIComponent(v.su) + "&body=" + encodeURIComponent(v.bd);
  }
  [to, subj, body].forEach(function (el) { if (el) el.addEventListener("input", refresh); });
  refresh();

  if (copy && copied) {
    copy.addEventListener("click", function () {
      var v = vals();
      var text = v.su + "\n\n" + v.bd;
      var done = function () { copied.hidden = false; setTimeout(function () { copied.hidden = true; }, 2000); };
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).then(done).catch(done);
      } else { done(); }
    });
  }
})();
```

- [ ] **Step 5: Add detail CSS**

Append to `site.css`:

```css
/* ===== Lead detail ===== */
.lead-detail { padding-bottom: 60px; }
.ld-head { display: flex; justify-content: space-between; align-items: baseline; margin: 6px 0 16px; }
.ld-co { font-family: var(--display); font-weight: 800; font-size: clamp(1.5rem, 3vw, 2rem); margin: 0; display: inline; }
.ld-status { font-size: 0.8rem; color: var(--body-subtle); }
.ld-grid { display: grid; grid-template-columns: 1.6fr 1fr; gap: 16px; align-items: start; }
.ld-col { display: flex; flex-direction: column; gap: 14px; }
.panel { background: #fff; border: 1px solid var(--line-soft); border-radius: 12px; padding: 16px 18px; }
.panel h4 { font-family: var(--display); font-size: 0.72rem; text-transform: uppercase; letter-spacing: .08em; color: var(--body-subtle); margin: 0 0 12px; }
.ld-actions { display: flex; gap: 8px; flex-wrap: wrap; margin-top: 12px; }
.ld-send { background: #FFF8F3; border-color: #F5CDB8; }
.ld-send-note { font-size: 0.82rem; color: var(--body-muted); margin: 0 0 12px; }
.ld-send-btns { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
.ld-marksent { margin-top: 14px; display: flex; gap: 10px; align-items: center; }
.ld-copied { color: #2E7D4F; }
.ld-why { color: #B3671A; font-weight: 600; font-size: 0.8rem; margin: 0 0 3px; }
.ld-why-text { color: var(--ink); font-size: 0.95rem; margin: 0 0 12px; line-height: 1.5; }
.ld-facts dt { font-size: 0.72rem; color: var(--body-subtle); }
.ld-facts dd { margin: 0 0 8px; font-size: 0.86rem; color: var(--body-muted); }
.ld-links { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 6px; }
.lnk { color: #E85A2E; text-decoration: none; font-size: 0.86rem; }
.lnk:hover { text-decoration: underline; }
@media (max-width: 900px) { .ld-grid { grid-template-columns: 1fr; } }
/* ===== end lead detail ===== */
```

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~LeadDetail"` → PASS. Full `dotnet test` → green.

- [ ] **Step 7: Commit**

```bash
git add src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml src/Coldstart.Web/wwwroot/js/lead-detail.js src/Coldstart.Web/wwwroot/css/site.css tests/Coldstart.Web.Tests/LeadDetailTests.cs
git commit -m "feat(app): two-column lead detail with status-driven actions and send handoff; fix mark-sent visibility"
```

---

### Task 6: Notes in the detail view (handlers + UI)

**Files:**
- Modify: `src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml.cs`
- Modify: `src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml`
- Modify: `src/Coldstart.Web/wwwroot/css/site.css`
- Test: `tests/Coldstart.Web.Tests/LeadDetailTests.cs`

**Interfaces:**
- Consumes: Task 3 `ILeadWorkflow.AddNoteAsync/UpdateNoteAsync/RemoveNoteAsync`, `LeadDetail.Notes`; Task 5 detail view (the `<!-- NOTES-PANEL -->` placeholder).
- Produces: `OnPostAddNoteAsync`/`OnPostUpdateNoteAsync`/`OnPostRemoveNoteAsync` handlers; the notes panel UI.

- [ ] **Step 1: Write the failing tests**

Add to `LeadDetailTests`:

```csharp
[Fact]
public async Task AddNote_PersistsAndRendersOnTheLead()
{
    // Arrange — seed a lead for the user; scrape antiforgery from GET /app/leads/{id}
    var token = /* antiforgery scraped from the detail GET — reuse the file's helper */ "";

    // Act
    var post = await client.PostAsync($"/app/leads/{id}?handler=AddNote", new FormUrlEncodedContent(
        new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["NoteText"] = "ring them Tuesday" }));
    var html = await client.GetStringAsync($"/app/leads/{id}");

    // Assert
    post.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found);
    html.Should().Contain("ring them Tuesday");
}

[Fact]
public async Task RemoveNote_RemovesItFromTheLead()
{
    // Arrange — add a note, then discover its id from the rendered delete form (name="NoteId")
    // (add via the AddNote POST, GET the page, extract the note id from the delete form's hidden input)

    // Act — POST RemoveNote with that id + token
    // Assert — GET shows the note text gone
}
```

Fill the RemoveNote arrange by parsing the rendered delete-form hidden `NoteId` value (the note id is a Guid the server generated). Reuse the file's antiforgery-scrape helper.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~LeadDetail"`
Expected: FAIL — no AddNote handler / no notes UI.

- [ ] **Step 3: Add the note handlers**

In `Detail.cshtml.cs`, extend `LeadInput` and add the three note handlers. They redirect back to the **detail** page (not `/app`) so a saved note is immediately visible — hence they use a dedicated `NoteActionAsync` helper rather than the existing `RunAsync` (which redirects to `/app`).

Add to `LeadInput`:

```csharp
        public string? NoteText { get; set; }
        public Guid NoteId { get; set; }
```

Add the handlers + helper:

```csharp
    public async Task<IActionResult> OnPostAddNoteAsync(Guid id, CancellationToken ct)
        => await NoteActionAsync(id, ct, cid => workflow.AddNoteAsync(cid, id, Guid.NewGuid(), Input.NoteText ?? "", ct));

    public async Task<IActionResult> OnPostUpdateNoteAsync(Guid id, CancellationToken ct)
        => await NoteActionAsync(id, ct, cid => workflow.UpdateNoteAsync(cid, id, Input.NoteId, Input.NoteText ?? "", ct));

    public async Task<IActionResult> OnPostRemoveNoteAsync(Guid id, CancellationToken ct)
        => await NoteActionAsync(id, ct, cid => workflow.RemoveNoteAsync(cid, id, Input.NoteId, ct));

    private async Task<IActionResult> NoteActionAsync(Guid id, CancellationToken ct, Func<Guid, Task<LeadActionResult>> action)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });
        var result = await action(user.CustomerId.Value);
        if (result.Status == LeadActionStatus.NotFound) return RedirectToPage("/Index", new { area = "App" });
        if (result.Status == LeadActionStatus.Invalid)
        {
            if (await LoadAsync(id, ct) is null) return RedirectToPage("/Index", new { area = "App" });
            ModelState.AddModelError(string.Empty, result.Message ?? "That note couldn't be saved.");
            return Page();
        }
        return RedirectToPage("/Leads/Detail", new { area = "App", id });
    }
```

- [ ] **Step 4: Render the notes panel**

Replace the `<!-- NOTES-PANEL: added in Task 6 -->` comment in `Detail.cshtml` with:

```html
      <section class="panel ld-notes">
        <h4>Your notes</h4>
        @if (lead.Notes.Count > 0)
        {
          <ul class="ld-note-list">
            @foreach (var note in lead.Notes)
            {
              <li class="ld-note">
                <p class="ld-note-text">@note.Text</p>
                <div class="ld-note-meta">
                  <span class="ts">@((note.UpdatedAt ?? note.CreatedAt).ToString("dd MMM · HH:mm", System.Globalization.CultureInfo.InvariantCulture))</span>
                  <form method="post" asp-page-handler="RemoveNote" class="ld-note-del">
                    <input type="hidden" asp-for="Input.NoteId" value="@note.Id">
                    <button type="submit" class="ld-note-x" aria-label="Delete note">Delete</button>
                  </form>
                </div>
              </li>
            }
          </ul>
        }
        <form method="post" asp-page-handler="AddNote" class="ld-note-add">
          <textarea asp-for="Input.NoteText" rows="2" placeholder="Private to you — 'called, follow up next week'…"></textarea>
          <button type="submit" class="btn btn-ghost btn-sm">Add note</button>
        </form>
      </section>
```

(Editing in place is out of scope for v1's UI — the spec's "edit" is satisfied by delete+re-add; the `UpdateNoteAsync` handler exists for a future inline-edit affordance and is covered by the Application test. If you want an inline edit control now, add an edit form per note posting to `UpdateNote` with the note id + new text — optional, not required to pass the tests.)

- [ ] **Step 5: Add notes CSS**

Append to `site.css`:

```css
/* ===== Lead notes ===== */
.ld-notes { background: #FCFAF4; }
.ld-note-list { list-style: none; padding: 0; margin: 0 0 12px; display: flex; flex-direction: column; gap: 10px; }
.ld-note { border-bottom: 1px solid var(--line-soft); padding-bottom: 8px; }
.ld-note-text { margin: 0 0 4px; font-size: 0.88rem; color: var(--ink); white-space: pre-wrap; }
.ld-note-meta { display: flex; justify-content: space-between; align-items: center; }
.ld-note-x { background: none; border: none; color: var(--body-subtle); font-size: 0.78rem; cursor: pointer; }
.ld-note-x:hover { color: #B3402A; }
.ld-note-add textarea { width: 100%; border: 1px solid var(--line-soft); border-radius: 8px; padding: 9px 11px; font-family: var(--body); font-size: 0.88rem; margin-bottom: 8px; }
/* ===== end lead notes ===== */
```

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~LeadDetail"` → PASS. Full `dotnet test` → green.

- [ ] **Step 7: Commit**

```bash
git add src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml.cs src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml src/Coldstart.Web/wwwroot/css/site.css tests/Coldstart.Web.Tests/LeadDetailTests.cs
git commit -m "feat(app): per-lead notes in the detail view (add/list/delete)"
```

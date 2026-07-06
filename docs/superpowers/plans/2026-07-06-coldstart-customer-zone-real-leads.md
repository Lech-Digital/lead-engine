# Coldstart Customer Zone — Real Leads + Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the logged-in `/app` zone show the operator's real harvested leads (no mocks) and redesign the leads view into a branded dashboard.

**Architecture:** Converge the operator's login-customer and the harvest-target customer via get-or-create-by-name (new `ICustomerRepository.GetByNameAsync`); stop seeding mock leads; enrich the `LeadSummary` read-model with subject/draft/source/score; rebuild the Razor `/app` view as brand-styled lead cards with an empty state. Per-user tenant isolation preserved.

**Tech Stack:** .NET 10, EF Core 10 (Npgsql/Postgres), Razor Pages, xUnit + NSubstitute + FluentAssertions 7.*, Testcontainers.PostgreSql.

## Global Constraints

- TFM `net10.0`; build **0 warnings**.
- Hexagonal: Web sees Application read-models (`LeadSummary`/`LeadInbox`), never Domain entities (Identity `UserManager`/`ApplicationUser` in Web UI is allowed).
- CS-DATA: repositories return `Task<T>`/`Task<IReadOnlyList<T>>`, async + `CancellationToken`.
- CS-MOD: impls `internal`; `[InternalsVisibleTo]` test-only (present in App & Infra).
- EF Core pinned **10.0.4**; Testcontainers ctor `new PostgreSqlBuilder("postgres:17")`.
- Testing: strict three-phase AAA; Testcontainers for DB-backed; build 0 warnings; **full solution green after every task**.
- Commit messages: **no `Co-Authored-By`/"Generated with" trailer** (project policy).
- Builds on `dotnet-port` (SP1 + Stage 2 + Stage 3). Repo-root `NuGet.Config` required; Docker running for integration tests.

---

## File Structure

```
src/Coldstart.Application/Ports/ICustomerRepository.cs        (add GetByNameAsync)
src/Coldstart.Application/Customers/ProvisionCustomer.cs      (get-or-create, no seeding)
src/Coldstart.Application/Harvest/NicheOptions.cs             (add TargetCustomerName)
src/Coldstart.Application/Harvest/HarvestLeads.cs             (target = TargetCustomerName)
src/Coldstart.Application/Leads/LeadSummary.cs                (add Subject/Draft/Source/Score)
src/Coldstart.Application/Leads/ListLeads.cs                  (map new fields)
src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs (GetByNameAsync)
src/Coldstart.Harvester/appsettings.json                     (Niche.TargetCustomerName)
src/Coldstart.Web/Areas/App/Pages/Index.cshtml               (redesign)
src/Coldstart.Web/wwwroot/css/site.css                       (dashboard styles)
tests: ProvisionCustomerTests, CustomerRepositoryTests, HarvestLeadsTests, ListLeadsTests/GetLeadInboxTests, CustomerZoneTests
```

---

### Task 1: Get-or-create customer by name + stop seeding mocks

**Files:**
- Modify: `src/Coldstart.Application/Ports/ICustomerRepository.cs`, `src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs`, `src/Coldstart.Application/Customers/ProvisionCustomer.cs`
- Test: `tests/Coldstart.Application.Tests/ProvisionCustomerTests.cs` (rewrite), `tests/Coldstart.Infrastructure.Tests/CustomerRepositoryTests.cs` (add), `tests/Coldstart.Web.Tests/CustomerZoneTests.cs` (update to insert real leads, not rely on seeds)

**Interfaces:**
- Produces: `ICustomerRepository.GetByNameAsync(string name, CancellationToken ct) : Task<Customer?>`; `ProvisionCustomer` now get-or-creates by name and seeds **no leads**.

- [ ] **Step 1: Add the port method + a failing repo test**

In `src/Coldstart.Application/Ports/ICustomerRepository.cs` add:

```csharp
    Task<Customer?> GetByNameAsync(string name, CancellationToken ct);
```

Add to `tests/Coldstart.Infrastructure.Tests/CustomerRepositoryTests.cs`:

```csharp
    [Fact]
    public async Task GetByNameAsync_ReturnsSavedCustomer_OrNull()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        var repo = new CustomerRepository(db);
        var customer = Customer.Create("sam@acme.com");
        await repo.AddAsync(customer, CancellationToken.None);

        // Act
        var found = await repo.GetByNameAsync("sam@acme.com", CancellationToken.None);
        var missing = await repo.GetByNameAsync("nobody@nowhere.com", CancellationToken.None);

        // Assert
        found.Should().NotBeNull();
        found!.Id.Should().Be(customer.Id);
        missing.Should().BeNull();
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `docker compose up -d db && dotnet test tests/Coldstart.Infrastructure.Tests --filter GetByNameAsync`
Expected: FAIL — `GetByNameAsync` missing.

- [ ] **Step 3: Implement `GetByNameAsync`**

In `src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs` add:

```csharp
    public async Task<Customer?> GetByNameAsync(string name, CancellationToken ct) =>
        await db.Customers.FirstOrDefaultAsync(c => c.Name == name, ct);
```

- [ ] **Step 4: Rewrite `ProvisionCustomer` — get-or-create, no seeding**

Replace `ProvisionCustomer.ProvisionAsync` body in `src/Coldstart.Application/Customers/ProvisionCustomer.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

internal sealed class ProvisionCustomer(ICustomerRepository customers) : IProvisionCustomer
{
    public async Task<Guid> ProvisionAsync(string customerName, CancellationToken ct)
    {
        var existing = await customers.GetByNameAsync(customerName, ct);
        if (existing is not null) return existing.Id.Value;

        var customer = Customer.Create(customerName);   // no mock leads — real leads come from the harvester
        await customers.AddAsync(customer, ct);
        return customer.Id.Value;
    }
}
```

- [ ] **Step 5: Rewrite `ProvisionCustomerTests`**

`tests/Coldstart.Application.Tests/ProvisionCustomerTests.cs`:

```csharp
using Coldstart.Application.Customers;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class ProvisionCustomerTests
{
    [Fact]
    public async Task ProvisionAsync_WhenNoExistingCustomer_CreatesOneWithNoLeads()
    {
        // Arrange
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetByNameAsync("sam@acme.com", Arg.Any<CancellationToken>()).Returns((Customer?)null);
        Customer? saved = null;
        await customers.AddAsync(Arg.Do<Customer>(c => saved = c), Arg.Any<CancellationToken>());
        var sut = new ProvisionCustomer(customers);

        // Act
        var id = await sut.ProvisionAsync("sam@acme.com", CancellationToken.None);

        // Assert
        saved.Should().NotBeNull();
        saved!.Leads.Should().BeEmpty();       // no mock seeding
        id.Should().Be(saved.Id.Value);
    }

    [Fact]
    public async Task ProvisionAsync_WhenCustomerWithNameExists_LinksToItWithoutCreating()
    {
        // Arrange
        var customers = Substitute.For<ICustomerRepository>();
        var existing = Customer.Create("sam@acme.com");
        customers.GetByNameAsync("sam@acme.com", Arg.Any<CancellationToken>()).Returns(existing);
        var sut = new ProvisionCustomer(customers);

        // Act
        var id = await sut.ProvisionAsync("sam@acme.com", CancellationToken.None);

        // Assert
        id.Should().Be(existing.Id.Value);
        await customers.DidNotReceive().AddAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 6: Update `CustomerZoneTests` to insert real leads (not seeded mocks)**

The zone tests currently rely on the mock seed ("Patronus AI"). Rewrite them to insert real leads into the registered user's customer via a service-scope + repository, and to assert the empty state for a user with no leads. Keep the tenant-isolation assertion. Example shape (adapt to the existing `RegisterAsync` helper + `ColdstartWebFactory`):

```csharp
    [Fact]
    public async Task App_ShowsRealLeadsForTheLoggedInUsersCustomer()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"zone-{Guid.NewGuid():N}@test.com";
        await RegisterAsync(client, email, "Passw0rd!");   // signs in; provisions a customer (no leads now)
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
            var user = await users.FindByEmailAsync(email);
            var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), "Patronus AI", Fit.Yes,
                "A thought after the $50M", "Saw your raise…", "funding", "https://x/1", "fp-zone-1", 0.7, "raised", DateTimeOffset.UtcNow);
            db.Leads.Add(lead);
            await db.SaveChangesAsync();
        }

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("Patronus AI");
    }

    [Fact]
    public async Task App_WhenNoLeads_ShowsEmptyState()
    {
        // Arrange
        var client = factory.CreateClient();
        await RegisterAsync(client, $"empty-{Guid.NewGuid():N}@test.com", "Passw0rd!");

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("No leads yet");
    }
```

Keep/adapt the existing tenant-isolation test: register two users, insert a distinctly-named lead into each user's customer, assert each `/app` shows only its own. (The "No leads yet" assertion depends on Task 4's empty-state markup; until then this specific test may be added in Task 4 — if so, note it. The real-leads + isolation tests work against the current plain table too, since it renders Company.)

- [ ] **Step 7: Run, build, commit**

Run: `dotnet test Coldstart.sln` → all green; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Application src/Coldstart.Infrastructure tests
git commit -m "feat(zone): get-or-create customer by name; stop seeding mock leads"
```

---

### Task 2: Harvest into the configured target customer

**Files:**
- Modify: `src/Coldstart.Application/Harvest/NicheOptions.cs`, `src/Coldstart.Application/Harvest/HarvestLeads.cs`, `src/Coldstart.Harvester/appsettings.json`
- Test: `tests/Coldstart.Application.Tests/HarvestLeadsTests.cs` (update)

**Interfaces:**
- Consumes: `ICustomerRepository.GetByNameAsync` (Task 1).
- Produces: `NicheOptions.TargetCustomerName` (string, default "Coldstart"); `HarvestLeads` saves under that customer (get-or-create by name).

- [ ] **Step 1: Add the config property**

In `src/Coldstart.Application/Harvest/NicheOptions.cs` add to `NicheOptions`:

```csharp
    public string TargetCustomerName { get; set; } = "Coldstart";
```

- [ ] **Step 2: Point the orchestrator at the configured customer via get-or-create**

In `src/Coldstart.Application/Harvest/HarvestLeads.cs`, replace the `HouseCustomerName` const + `ResolveHouseCustomerAsync` with resolution by config using `GetByNameAsync`:

```csharp
    private async Task<Customer> ResolveTargetCustomerAsync(CancellationToken ct)
    {
        var existing = await customers.GetByNameAsync(niche.TargetCustomerName, ct);
        if (existing is not null) return existing;
        var created = Customer.Create(niche.TargetCustomerName);
        await customers.AddAsync(created, ct);
        return created;
    }
```

and update the call site in `RunAsync` (`var house = await ResolveHouseCustomerAsync(ct);` → `var target = await ResolveTargetCustomerAsync(ct);` and use `target.Id`). Remove the now-unused `AllAsync`-based lookup.

- [ ] **Step 3: Update `HarvestLeadsTests`**

The existing unit tests stub `ICustomerRepository`. Update the `customers.AllAsync(...)` stubs to `customers.GetByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Customer?)null)` (so the orchestrator creates the target), and set the niche's `TargetCustomerName` (default "Coldstart" is fine). Assert the saved lead's `CustomerId` equals the created target customer's Id (capture it via `AddAsync(Arg.Do<Customer>(...))`).

```csharp
        // in each HarvestLeadsTests arrange block, replace the AllAsync stub:
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        Customer? target = null;
        await customers.AddAsync(Arg.Do<Customer>(c => target = c), Arg.Any<CancellationToken>());
```

Also update the Infrastructure `HarvestEndToEndTests` if it asserted the customer name "Coldstart" — the default `TargetCustomerName` is still "Coldstart", so the e2e assertion `Customers.SingleAsync(c => c.Name == "Coldstart")` still holds.

- [ ] **Step 4: Set the Harvester's target to your account email**

In `src/Coldstart.Harvester/appsettings.json`, add to the `Niche` object: `"TargetCustomerName": "andrzej.lech.andrzej@gmail.com"` (the operator's login email, so harvested leads land in the account that logs in). Document in the Harvester README section that this must match the login email.

- [ ] **Step 5: Run, build, commit**

Run: `dotnet test Coldstart.sln` → all green; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Application src/Coldstart.Harvester tests
git commit -m "feat(harvest): save into the configured target customer (converges with the operator login)"
```

---

### Task 3: Enrich the LeadSummary read-model (subject/draft/source/score)

**Files:**
- Modify: `src/Coldstart.Application/Leads/LeadSummary.cs`, `src/Coldstart.Application/Leads/ListLeads.cs`
- Test: `tests/Coldstart.Application.Tests/ListLeadsTests.cs`, `GetLeadInboxTests.cs` (update assertions)

**Interfaces:**
- Produces: `LeadSummary(Guid Id, string Company, string Fit, string Status, string? RecipientEmail, string EmailConfidence, string Subject, string Draft, string Source, double Score)`.

- [ ] **Step 1: Extend the record + update the failing test**

`src/Coldstart.Application/Leads/LeadSummary.cs`:

```csharp
namespace Coldstart.Application.Leads;

public sealed record LeadSummary(
    Guid Id,
    string Company,
    string Fit,
    string Status,
    string? RecipientEmail,
    string EmailConfidence,
    string Subject,
    string Draft,
    string Source,
    double Score);
```

Update `tests/Coldstart.Application.Tests/ListLeadsTests.cs` — the existing happy-path test constructs a `Lead` and asserts the summary. Add assertions for the new fields (build the lead via `Lead.CreateHarvested(...)` so Subject/Draft/Source/Score are set), e.g.:

```csharp
        result[0].Subject.Should().Be("A thought after the $50M");
        result[0].Source.Should().Be("funding");
        result[0].Score.Should().Be(0.7);
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Application.Tests --filter "ListLeads|GetLeadInbox"`
Expected: FAIL — `LeadSummary` ctor arity changed / new fields unset.

- [ ] **Step 3: Map the new fields in `ListLeads`**

In `src/Coldstart.Application/Leads/ListLeads.cs`, extend the projection:

```csharp
        return found
            .Select(l => new LeadSummary(
                l.Id.Value, l.Company, l.Fit.ToString(), l.Status.ToString(),
                l.RecipientEmail?.Value, l.EmailConfidence.ToString(),
                l.Subject, l.Draft, l.Source, l.Score))
            .ToList();
```

- [ ] **Step 4: Run to green, build, commit**

Run: `dotnet test tests/Coldstart.Application.Tests --filter "ListLeads|GetLeadInbox"` → PASS; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Application tests/Coldstart.Application.Tests
git commit -m "feat(zone): enrich LeadSummary read-model with subject/draft/source/score"
```

---

### Task 4: Redesign the /app leads dashboard

**Files:**
- Modify: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml`, `src/Coldstart.Web/wwwroot/css/site.css`
- Test: `tests/Coldstart.Web.Tests/CustomerZoneTests.cs` (assert card markup + empty state)

**Interfaces:**
- Consumes: `LeadSummary` (subject/draft/source/score), `IGetLeadInbox`.

- [ ] **Step 1: Add the empty-state + card assertions to CustomerZoneTests**

Ensure `CustomerZoneTests` has: (a) the real-leads test now also asserts the **subject** renders (e.g. `html.Should().Contain("A thought after the $50M")`) and a fit badge/source chip marker (e.g. `html.Should().Contain("app-lead")`), and (b) the empty-state test asserts `html.Should().Contain("No leads yet")`. (Insert leads via the repository as in Task 1.)

- [ ] **Step 2: Run to verify the new assertions fail**

Run: `dotnet test tests/Coldstart.Web.Tests --filter CustomerZoneTests`
Expected: FAIL — current plain table has no `app-lead` markup / no "No leads yet".

- [ ] **Step 3: Rebuild the view**

`src/Coldstart.Web/Areas/App/Pages/Index.cshtml`:

```html
@page "/app"
@model Coldstart.Web.Areas.App.Pages.IndexModel
@{ ViewData["Title"] = "Your leads"; Layout = "/Pages/Shared/_Layout.cshtml"; }

<main class="wrap app-zone">
  <header class="app-head">
    <div>
      <p class="eyebrow">Customer zone</p>
      <h1>@Model.CustomerName</h1>
      <p class="app-sub">@Model.Leads.Count qualified lead@(Model.Leads.Count == 1 ? "" : "s") from your morning harvest.</p>
    </div>
    <form method="post" asp-page="/Account/Logout" asp-area="Identity"><button class="btn btn-ghost" type="submit">Log out</button></form>
  </header>

  @if (Model.Leads.Count == 0)
  {
    <div class="app-empty">
      <h2>No leads yet</h2>
      <p>Your next harvest will fill this in — freshly qualified companies with the first email already drafted.</p>
    </div>
  }
  else
  {
    <div class="app-leads">
      @foreach (var lead in Model.Leads)
      {
        <article class="app-lead">
          <div class="app-lead-top">
            <h3>@lead.Company</h3>
            <span class="fit fit-@lead.Fit.ToLower()">@lead.Fit</span>
          </div>
          <div class="app-lead-meta">
            <span class="chip">@lead.Source</span>
            <span class="score">score @lead.Score.ToString("0.00")</span>
          </div>
          <p class="app-lead-subj">@lead.Subject</p>
          <details class="app-lead-draft">
            <summary>Preview draft</summary>
            <p>@lead.Draft</p>
          </details>
          <div class="app-lead-foot">
            <span class="to">@(string.IsNullOrEmpty(lead.RecipientEmail) ? "—" : lead.RecipientEmail)</span>
            <span class="conf conf-@lead.EmailConfidence.ToLower()">@lead.EmailConfidence</span>
            <span class="status">@lead.Status</span>
          </div>
        </article>
      }
    </div>
  }
</main>
```

- [ ] **Step 4: Add dashboard styles to `site.css`**

Append (reuse existing brand vars `--cream`, `--cream-2`, `--ink`, `--ink-soft`, `--line-2`, `--orange-d`, `--teal`, `--gold`, `--mono`, `--sans`):

```css
  /* customer zone (/app) */
  .app-zone { padding: 40px 0 64px; }
  .app-head { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; margin-bottom: 28px; }
  .app-head h1 { margin: 4px 0 2px; font-size: 1.7rem; letter-spacing: -0.02em; }
  .app-sub { color: var(--ink-soft); margin: 0; font-size: 0.95rem; }
  .app-empty { background: var(--cream-2); border: 1.5px solid var(--line-2); border-radius: 16px; padding: 44px 28px; text-align: center; }
  .app-empty h2 { margin: 0 0 8px; }
  .app-empty p { color: var(--ink-soft); margin: 0; }
  .app-leads { display: grid; gap: 16px; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); }
  .app-lead { background: var(--cream-2); border: 1.5px solid var(--line-2); border-radius: 14px; box-shadow: 0 4px 0 var(--line-2); padding: 18px 18px 16px; display: flex; flex-direction: column; gap: 10px; }
  .app-lead-top { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
  .app-lead-top h3 { margin: 0; font-size: 1.08rem; letter-spacing: -0.01em; }
  .fit { font-family: var(--mono); font-size: 0.68rem; text-transform: uppercase; letter-spacing: 0.08em; font-weight: 700; padding: 3px 9px; border-radius: 999px; }
  .fit-yes { background: rgba(60,122,112,0.16); color: #2c6a60; }
  .fit-maybe { background: rgba(176,120,20,0.16); color: #8a5e10; }
  .app-lead-meta { display: flex; align-items: center; gap: 10px; font-family: var(--mono); font-size: 0.72rem; color: var(--ink-soft); }
  .chip { background: var(--cream); border: 1px solid var(--line-2); border-radius: 6px; padding: 2px 8px; text-transform: uppercase; letter-spacing: 0.06em; }
  .app-lead-subj { margin: 2px 0 0; font-weight: 700; color: var(--ink); }
  .app-lead-draft summary { cursor: pointer; font-size: 0.85rem; color: var(--orange-d); font-weight: 700; }
  .app-lead-draft p { white-space: pre-wrap; font-size: 0.9rem; color: var(--ink); margin: 8px 0 0; }
  .app-lead-foot { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; margin-top: 4px; padding-top: 10px; border-top: 1px solid var(--line-2); font-size: 0.78rem; color: var(--ink-soft); }
  .app-lead-foot .to { font-family: var(--mono); }
  .conf { font-family: var(--mono); font-size: 0.66rem; text-transform: uppercase; letter-spacing: 0.06em; padding: 2px 7px; border-radius: 6px; }
  .conf-verified { background: rgba(60,122,112,0.16); color: #2c6a60; }
```

(If a brand var name differs in `site.css`, use the actual token — check the `:root` block. `.btn`/`.btn-ghost`/`.wrap`/`.eyebrow` already exist.)

- [ ] **Step 5: Run to green, build, commit**

Run: `dotnet test tests/Coldstart.Web.Tests --filter CustomerZoneTests` → PASS; `dotnet test Coldstart.sln` → all green; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Web tests/Coldstart.Web.Tests
git commit -m "feat(zone): redesign /app leads dashboard (cards, fit badges, draft preview, empty state)"
```

---

## Self-Review

**Spec coverage:**
- Remove mock seeding → Task 1. ✓
- Get-or-create by name; converge login + harvest customer → Tasks 1 (provision) + 2 (harvest target). ✓
- `GetByNameAsync` port + impl (replaces AllAsync scan) → Task 1. ✓
- `NicheOptions.TargetCustomerName` (operator email) → Task 2. ✓
- Richer read-model (subject/draft/source/score), Web sees only read-models → Task 3. ✓
- `/app` redesign: header + count, cards, FIT badges, source chip, score, draft expander, meta, empty state → Task 4. ✓
- Tests: provision (no seed + get-or-create), GetByName repo, harvest target, read-model mapping, zone real-leads + empty-state + isolation → Tasks 1–4. ✓
- Non-goals (multi-niche, FIT:NO fix) untouched. ✓

**Placeholder scan:** none — full code/markup/CSS given. The CSS note ("if a var name differs, use the actual token") points at the concrete `:root` block, not a placeholder.

**Type consistency:** `ICustomerRepository.GetByNameAsync(string, ct) → Task<Customer?>`, `ProvisionCustomer.ProvisionAsync(string, ct) → Guid`, `NicheOptions.TargetCustomerName`, `HarvestLeads.ResolveTargetCustomerAsync`, `LeadSummary(Id,Company,Fit,Status,RecipientEmail,EmailConfidence,Subject,Draft,Source,Score)`, `ListLeads` projection — consistent across Tasks 1–4. Task 4's view reads exactly the `LeadSummary` fields added in Task 3.

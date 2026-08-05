# Tester Feedback Round 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the four fixes from tester feedback round 1: reopen-skipped-leads UI fix, complimentary plan access (grant + expiry + email), sidebar navigation shell for both panels, and generation-level draft variance.

**Architecture:** Hexagonal .NET 10 solution (`Coldstart.Domain` ← `Coldstart.Application` ← `Coldstart.Infrastructure` / `Coldstart.Web` Razor Pages). All UI work stays in `Coldstart.Web`; the complimentary-plan feature adds a domain method, one EF column, an Application use case + sweeper, an admin API endpoint and a CLI verb; draft variance lives in `GroqLeadEnricher` (Infrastructure) plus a new lookup port.

**Tech Stack:** .NET 10, Razor Pages, EF Core + SQLite, xUnit + NSubstitute + FluentAssertions, Groq LLM API, LangSearch.

**Spec:** `docs/superpowers/specs/2026-08-05-tester-feedback-round1-design.md`

## Global Constraints

- Domain project: zero project/package dependencies (CS-HEX-01).
- Ports live in Application, not Infrastructure (CS-HEX-02). DI in composition root only.
- `internal` over `public` for implementation classes; async all the way with `CancellationToken` through IO signatures.
- Tests: xUnit + NSubstitute + FluentAssertions, `MethodName_StateUnderTest_ExpectedBehavior` naming, strict `// Arrange` / `// Act` / `// Assert`, ≥1 happy + 2 sad paths per new class. Never mock domain entities.
- Commit messages: conventional (`fix:`, `feat:`, `refactor:`, `test:`); NO Co-Authored-By trailer.
- Run tests from repo root: `dotnet test` (full) or `dotnet test tests/<Project> --filter <Name>`.
- Copy rules: customer-facing text is warm, plain, honest — no marketing voice.
- Implementation order is Phase 1 → 4 (quick wins first). Tasks inside a phase are sequential.

---

## Phase 1 — Quick wins (reopen fix, scoring label)

### Task 1: Reopen skipped leads from History

The skip confirmation promises "you can reopen it anytime", but skipped rows in History render no reopen control and are not clickable. Backend (`OnPostReopenAsync` → `LeadWorkflow.ReopenAsync` → `Lead.Reopen()`) already works — this is view-only.

**Files:**
- Modify: `src/Coldstart.Web/Areas/App/Pages/History.cshtml:39-73`
- Test: `tests/Coldstart.Web.Tests/CustomerHistoryTests.cs`

**Interfaces:**
- Consumes: existing `OnPostReopenAsync(Guid id, ...)` handler on `HistoryModel`; existing `LeadSummary` (`Id`, `Company`, `Status`, `SkipReason`, …).
- Produces: nothing new — markup only.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Coldstart.Web.Tests/CustomerHistoryTests.cs` (same class, reuse the existing `AddLeadAsync`/`LeadIdAsync`/`TestAuth` helpers already in that file):

```csharp
[Fact]
public async Task History_SkippedRow_ShowsReopenButton()
{
    // Arrange
    var client = factory.CreateClient();
    var email = $"hist-skipbtn-{Guid.NewGuid():N}@test.com";
    await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
    await AddLeadAsync(email, "Skipped Co", LeadStatus.Shelved);
    var id = await LeadIdAsync(email, "Skipped Co");

    // Act
    var html = await client.GetStringAsync("/app/history?filter=skipped");

    // Assert — the reopen form posts the skipped lead's id
    html.Should().Contain("handler=Reopen");
    html.Should().Contain($"value=\"{id}\"");
}

[Fact]
public async Task History_Rows_LinkToLeadDetail()
{
    // Arrange
    var client = factory.CreateClient();
    var email = $"hist-link-{Guid.NewGuid():N}@test.com";
    await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
    await AddLeadAsync(email, "Linked Co", LeadStatus.Sent);
    var id = await LeadIdAsync(email, "Linked Co");

    // Act
    var html = await client.GetStringAsync("/app/history");

    // Assert
    html.Should().Contain($"href=\"/app/leads/{id}\"");
}
```

Note: if the existing reopen form in `History.cshtml` renders `asp-page-handler` as `?handler=Reopen` in a `formaction`/`action` attribute, match the exact rendered shape — run the existing `Reopen_MovesOlderLeadBackToTheQueue` test's page first if unsure. The assertion may need `html.Should().Contain("cz-reopen-form")` plus the id assertion instead; pick assertions that pin behavior (form exists for this lead id), not incidental markup.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "History_SkippedRow_ShowsReopenButton|History_Rows_LinkToLeadDetail"`
Expected: both FAIL (skipped rows have no reopen form; no detail links exist).

- [ ] **Step 3: Update the view**

In `src/Coldstart.Web/Areas/App/Pages/History.cshtml`, inside the `@foreach (var lead in Model.Rows)` loop:

1. Make the company + subject clickable. Replace:

```html
          <div class="cz-main">
            <div class="cz-head">
              <span class="cz-co">@lead.Company</span>
```

with:

```html
          <div class="cz-main">
            <div class="cz-head">
              <a class="cz-co cz-co-link" href="/app/leads/@lead.Id">@lead.Company</a>
```

and make the subject line a link too:

```html
            <div class="cz-subj"><a class="cz-subj-link" href="/app/leads/@lead.Id">@lead.Subject</a></div>
```

(Do NOT wrap the whole `.cz-row` in an `<a>` — the reopen `<form>` cannot legally nest inside an anchor.)

2. Add the reopen form to skipped rows — extend the `else if (lead.Status is "Shelved" or "Rejected")` branch:

```html
              else if (lead.Status is "Shelved" or "Rejected")
              {
                <span class="cz-stbadge st-skip">SKIPPED · @(lead.SkipReason ?? (lead.Status == "Shelved" ? "you" : "not a fit"))</span>
                <form method="post" asp-page-handler="Reopen" class="cz-reopen-form">
                  <input type="hidden" name="id" value="@lead.Id">
                  <button class="cz-why" type="submit">reopen →</button>
                </form>
              }
```

3. Add link styling to `src/Coldstart.Web/wwwroot/css/site.css` next to the existing `.cz-co` rules (search for `.cz-co`):

```css
.cz-co-link, .cz-subj-link { color: inherit; text-decoration: none; }
.cz-co-link:hover, .cz-subj-link:hover { text-decoration: underline; }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "CustomerHistoryTests"`
Expected: ALL PASS (new tests plus the pre-existing history tests — the markup change must not break `Reopen_MovesOlderLeadBackToTheQueue`).

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Web/Areas/App/Pages/History.cshtml src/Coldstart.Web/wwwroot/css/site.css tests/Coldstart.Web.Tests/CustomerHistoryTests.cs
git commit -m "fix(history): skipped leads get the promised reopen button; rows link to lead detail"
```

### Task 2: Honest baseline score label

All funding-only leads score exactly `BaseScore` (0.50) with zero matched signals; the detail page shows `0.50 · none`-style output that reads like a quality verdict. Label the floor honestly.

**Files:**
- Modify: `src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml:183-187`
- Test: `tests/Coldstart.Web.Tests/LeadDetailTests.cs`

**Interfaces:**
- Consumes: `LeadSummary.Score` (double), `LeadSummary.MatchedSignals` (string, comma-joined, empty when none).
- Produces: nothing new — markup only.

- [ ] **Step 1: Write the failing test**

Add to `tests/Coldstart.Web.Tests/LeadDetailTests.cs`, following that file's existing arrange helpers (open the file first and reuse its sign-in + lead-seeding pattern; leads seeded with no matched signals):

```csharp
[Fact]
public async Task Detail_NoMatchedSignals_ShowsBaselineScoreLabel()
{
    // Arrange — a lead whose score came only from the source floor (no keyword hits)
    var client = factory.CreateClient();
    var email = $"detail-base-{Guid.NewGuid():N}@test.com";
    await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");
    var id = await AddLeadAsync(email, "Floor Co");   // seeded with MatchedSignals = ""

    // Act
    var html = await client.GetStringAsync($"/app/leads/{id}");

    // Assert
    html.Should().Contain("baseline — source signal only");
}
```

(Adjust the seeding call to whatever helper `LeadDetailTests.cs` actually uses — the lead must end up with an empty `MatchedSignals` string.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "Detail_NoMatchedSignals_ShowsBaselineScoreLabel"`
Expected: FAIL — the label doesn't exist yet.

- [ ] **Step 3: Update the score row in the facts list**

In `src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml`, replace:

```html
          <div><dt>Score</dt><dd>@lead.Score.ToString("0.00") · @lead.EmailConfidence.ToLower()</dd></div>
```

with:

```html
          <div><dt>Score</dt><dd>@lead.Score.ToString("0.00")@(string.IsNullOrWhiteSpace(lead.MatchedSignals) ? " · baseline — source signal only" : "") · @lead.EmailConfidence.ToLower()</dd></div>
```

Also fix the empty "Signals" row in the same `<dl>` — replace:

```html
          <div><dt>Signals</dt><dd class="ts">@lead.MatchedSignals</dd></div>
```

with:

```html
          <div><dt>Signals</dt><dd class="ts">@(string.IsNullOrWhiteSpace(lead.MatchedSignals) ? "none beyond the source itself" : lead.MatchedSignals)</dd></div>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "LeadDetailTests"`
Expected: ALL PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml tests/Coldstart.Web.Tests/LeadDetailTests.cs
git commit -m "fix(leads): label floor scores honestly instead of implying a quality verdict"
```

---

## Phase 2 — Complimentary access

### Task 3: Domain — `ComplimentaryUntil` + `GrantComplimentary`

**Files:**
- Modify: `src/Coldstart.Domain/Customer.cs`
- Test: `tests/Coldstart.Domain.Tests/CustomerBillingTests.cs` (existing file — plan/subscription behavior already lives there; add these facts to that class)

**Interfaces:**
- Produces: `Customer.ComplimentaryUntil` (`DateTimeOffset?`, private set), `Customer.GrantComplimentary(DateTimeOffset until, DateTimeOffset now)` (throws `DomainException` on guard violations). `ActivateFull` and `DowngradeToFree` clear `ComplimentaryUntil`. Later tasks (sweep, endpoint) rely on these exact names.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void GrantComplimentary_FreeCustomer_BecomesFullUntilDate()
{
    // Arrange
    var customer = Customer.Create("Independent");
    var now = DateTimeOffset.UtcNow;
    var until = now.AddDays(5);

    // Act
    customer.GrantComplimentary(until, now);

    // Assert
    customer.Plan.Should().Be(CustomerPlan.Full);
    customer.ComplimentaryUntil.Should().Be(until);
    customer.SubscriptionEndedAt.Should().BeNull();
}

[Fact]
public void GrantComplimentary_WithLiveStripeSubscription_Throws()
{
    // Arrange
    var customer = Customer.Create("Paying Co");
    customer.ActivateFull("sub_123", DateTimeOffset.UtcNow.AddMonths(1));
    var now = DateTimeOffset.UtcNow;

    // Act
    Action act = () => customer.GrantComplimentary(now.AddDays(5), now);

    // Assert
    act.Should().Throw<DomainException>().WithMessage("*already has a paid subscription*");
}

[Fact]
public void GrantComplimentary_UntilInThePast_Throws()
{
    // Arrange
    var customer = Customer.Create("Late Co");
    var now = DateTimeOffset.UtcNow;

    // Act
    Action act = () => customer.GrantComplimentary(now.AddDays(-1), now);

    // Assert
    act.Should().Throw<DomainException>().WithMessage("*must be in the future*");
}

[Fact]
public void ActivateFull_DuringComplimentaryTrial_ClearsComplimentaryUntil()
{
    // Arrange
    var customer = Customer.Create("Converted Co");
    var now = DateTimeOffset.UtcNow;
    customer.GrantComplimentary(now.AddDays(5), now);

    // Act
    customer.ActivateFull("sub_456", now.AddMonths(1));

    // Assert
    customer.ComplimentaryUntil.Should().BeNull();
    customer.Plan.Should().Be(CustomerPlan.Full);
}

[Fact]
public void DowngradeToFree_AfterComplimentaryTrial_ClearsTrialAndKeepsRetentionClockUnset()
{
    // Arrange — a comp trial never paid, so the 60-day retention clock must not start
    var customer = Customer.Create("Lapsed Co");
    var now = DateTimeOffset.UtcNow;
    customer.GrantComplimentary(now.AddDays(5), now);

    // Act
    customer.DowngradeToFree(endedAt: null);

    // Assert
    customer.Plan.Should().Be(CustomerPlan.Free);
    customer.ComplimentaryUntil.Should().BeNull();
    customer.SubscriptionEndedAt.Should().BeNull();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Coldstart.Domain.Tests --filter "GrantComplimentary|ActivateFull_DuringComplimentaryTrial|DowngradeToFree_AfterComplimentaryTrial"`
Expected: FAIL — compile error, `ComplimentaryUntil`/`GrantComplimentary` don't exist.

- [ ] **Step 3: Implement on `Customer`**

In `src/Coldstart.Domain/Customer.cs`:

Add the property below `PlanRenewsAt` (line 15):

```csharp
    // Complimentary (non-Stripe) full access, e.g. tester thank-yous. Null = none.
    // While set, Plan is Full; the nightly sweep downgrades once it lapses.
    public DateTimeOffset? ComplimentaryUntil { get; private set; }
```

Add the method after `ActivateFull`:

```csharp
    public void GrantComplimentary(DateTimeOffset until, DateTimeOffset now)
    {
        if (StripeSubscriptionId is not null)
            throw new DomainException("This customer already has a paid subscription — nothing to grant.");
        if (until <= now)
            throw new DomainException("The complimentary end date must be in the future.");
        Plan = CustomerPlan.Full;
        ComplimentaryUntil = until;
    }
```

In `ActivateFull(...)` add one line (a real subscription supersedes the trial):

```csharp
        ComplimentaryUntil = null;
```

In `DowngradeToFree(...)` add one line before `Plan = CustomerPlan.Free;`:

```csharp
        ComplimentaryUntil = null;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Coldstart.Domain.Tests`
Expected: ALL PASS (including all pre-existing domain tests).

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Domain/Customer.cs tests/Coldstart.Domain.Tests/CustomerTests.cs
git commit -m "feat(domain): complimentary full access with expiry date and Stripe guards"
```

### Task 4: EF mapping + migration

**Files:**
- Modify: `src/Coldstart.Infrastructure/Configurations/CustomerConfiguration.cs`
- Create: `src/Coldstart.Infrastructure/Migrations/<timestamp>_AddCustomerComplimentaryUntil.cs` (generated)

**Interfaces:**
- Produces: `ComplimentaryUntil` column on `customers`; persists via existing `ICustomerRepository.UpdateAsync`.

- [ ] **Step 1: Map the property**

In `CustomerConfiguration.Configure`, after `builder.Property(c => c.PlanRenewsAt);` add:

```csharp
        builder.Property(c => c.ComplimentaryUntil);
```

- [ ] **Step 2: Generate the migration**

Follow the repo's established procedure (see prior migrations such as `20260731075303_AddCustomerEnabledSources` and the notes in `docs/superpowers/plans/2026-07-23-model-level-tenant-separation.md:612`):

```bash
dotnet ef migrations add AddCustomerComplimentaryUntil \
  -p src/Coldstart.Infrastructure -s src/Coldstart.Infrastructure -o Migrations
```

If `dotnet ef` complains about a missing startup project, the repo's `DesignTimeDbContextFactory` handles it — check the exact `-s` used by the most recent migration commit (`git log --oneline --diff-filter=A -- 'src/Coldstart.Infrastructure/Migrations/*AddCustomerEnabledSources*'` then `git show <sha> --stat`).

- [ ] **Step 3: Verify the migration content and that the suite still passes**

The generated `Up()` must contain exactly one `AddColumn<DateTimeOffset>` for `ComplimentaryUntil` on `customers`, nullable. Then:

Run: `dotnet test`
Expected: ALL PASS (repository round-trip tests run against the migrated schema).

- [ ] **Step 4: Commit**

```bash
git add src/Coldstart.Infrastructure/Configurations/CustomerConfiguration.cs src/Coldstart.Infrastructure/Migrations/
git commit -m "feat(db): ComplimentaryUntil column on customers"
```

### Task 5: Application — grant use case + expiry sweeper

**Files:**
- Create: `src/Coldstart.Application/Billing/GrantComplimentaryAccess.cs`
- Create: `src/Coldstart.Application/Billing/ComplimentaryExpirySweeper.cs`
- Modify: `src/Coldstart.Application/DependencyInjection.cs` (or wherever Application services register — find `ApplySubscriptionChange`'s registration and mirror it)
- Test: `tests/Coldstart.Application.Tests/GrantComplimentaryAccessTests.cs`, `tests/Coldstart.Application.Tests/ComplimentaryExpirySweeperTests.cs`

**Interfaces:**
- Consumes: `ICustomerRepository.GetAsync/AllAsync/UpdateAsync`, `Customer.GrantComplimentary(until, now)`, `Customer.DowngradeToFree(null)`.
- Produces:
  - `IGrantComplimentaryAccess` with `Task<ComplimentaryGrantResult> ExecuteAsync(Guid customerId, int days, CancellationToken ct)`.
  - `ComplimentaryGrantResult(bool Succeeded, string Message, string? CustomerName, DateTimeOffset? Until)`.
  - `ComplimentaryExpirySweeper` (public, resolved by the hosted service) with `Task<int> SweepAsync(DateTimeOffset now, CancellationToken ct)` returning the number downgraded.

- [ ] **Step 1: Write the failing tests**

`tests/Coldstart.Application.Tests/GrantComplimentaryAccessTests.cs`:

```csharp
using Coldstart.Application.Billing;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class GrantComplimentaryAccessTests
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();

    [Fact]
    public async Task ExecuteAsync_FreeCustomer_GrantsAndPersists()
    {
        // Arrange
        var customer = Customer.Create("Independent");
        _customers.GetAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new GrantComplimentaryAccess(_customers);

        // Act
        var result = await sut.ExecuteAsync(customer.Id.Value, 5, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Until.Should().NotBeNull();
        customer.Plan.Should().Be(CustomerPlan.Full);
        await _customers.Received(1).UpdateAsync(customer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_UnknownCustomer_ReturnsNotFound()
    {
        // Arrange
        _customers.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        var sut = new GrantComplimentaryAccess(_customers);

        // Act
        var result = await sut.ExecuteAsync(Guid.NewGuid(), 5, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("No such customer");
    }

    [Fact]
    public async Task ExecuteAsync_NonPositiveDays_ReturnsInvalid()
    {
        // Arrange
        var sut = new GrantComplimentaryAccess(_customers);

        // Act
        var result = await sut.ExecuteAsync(Guid.NewGuid(), 0, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("at least 1 day");
    }

    [Fact]
    public async Task ExecuteAsync_StripeSubscriber_ReturnsDomainGuardMessage()
    {
        // Arrange
        var customer = Customer.Create("Paying Co");
        customer.ActivateFull("sub_1", null);
        _customers.GetAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new GrantComplimentaryAccess(_customers);

        // Act
        var result = await sut.ExecuteAsync(customer.Id.Value, 5, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("already has a paid subscription");
        await _customers.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }
}
```

`tests/Coldstart.Application.Tests/ComplimentaryExpirySweeperTests.cs`:

```csharp
using Coldstart.Application.Billing;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class ComplimentaryExpirySweeperTests
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Customer CompCustomer(string name, DateTimeOffset until)
    {
        var c = Customer.Create(name);
        c.GrantComplimentary(until, until.AddDays(-10));   // grant made 10 days before `until`
        return c;
    }

    [Fact]
    public async Task SweepAsync_LapsedTrial_DowngradesWithoutRetentionClock()
    {
        // Arrange
        var lapsed = CompCustomer("Lapsed Co", Now.AddDays(-1));
        _customers.AllAsync(Arg.Any<CancellationToken>()).Returns([lapsed]);
        var sut = new ComplimentaryExpirySweeper(_customers);

        // Act
        var count = await sut.SweepAsync(Now, CancellationToken.None);

        // Assert
        count.Should().Be(1);
        lapsed.Plan.Should().Be(CustomerPlan.Free);
        lapsed.ComplimentaryUntil.Should().BeNull();
        lapsed.SubscriptionEndedAt.Should().BeNull();
        await _customers.Received(1).UpdateAsync(lapsed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SweepAsync_ActiveTrial_LeavesCustomerAlone()
    {
        // Arrange
        var active = CompCustomer("Active Co", Now.AddDays(3));
        _customers.AllAsync(Arg.Any<CancellationToken>()).Returns([active]);
        var sut = new ComplimentaryExpirySweeper(_customers);

        // Act
        var count = await sut.SweepAsync(Now, CancellationToken.None);

        // Assert
        count.Should().Be(0);
        active.Plan.Should().Be(CustomerPlan.Full);
        await _customers.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SweepAsync_TrialConvertedToStripe_IsNotDowngraded()
    {
        // Arrange — subscribed mid-trial; ActivateFull cleared ComplimentaryUntil,
        // but defend against a lapsed date coexisting with a live subscription anyway.
        var converted = CompCustomer("Converted Co", Now.AddDays(-1));
        converted.ActivateFull("sub_9", Now.AddMonths(1));
        _customers.AllAsync(Arg.Any<CancellationToken>()).Returns([converted]);
        var sut = new ComplimentaryExpirySweeper(_customers);

        // Act
        var count = await sut.SweepAsync(Now, CancellationToken.None);

        // Assert
        count.Should().Be(0);
        converted.Plan.Should().Be(CustomerPlan.Full);
    }

    [Fact]
    public async Task SweepAsync_OneRowFails_OthersStillSwept()
    {
        // Arrange
        var bad = CompCustomer("Bad Co", Now.AddDays(-2));
        var good = CompCustomer("Good Co", Now.AddDays(-1));
        _customers.AllAsync(Arg.Any<CancellationToken>()).Returns([bad, good]);
        _customers.UpdateAsync(bad, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("db hiccup"));
        var sut = new ComplimentaryExpirySweeper(_customers);

        // Act
        var count = await sut.SweepAsync(Now, CancellationToken.None);

        // Assert — bad row is skipped, good row still downgraded
        count.Should().Be(1);
        good.Plan.Should().Be(CustomerPlan.Free);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Coldstart.Application.Tests --filter "GrantComplimentaryAccess|ComplimentaryExpirySweeper"`
Expected: FAIL — compile errors, the classes don't exist.

- [ ] **Step 3: Implement**

`src/Coldstart.Application/Billing/GrantComplimentaryAccess.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Billing;

public sealed record ComplimentaryGrantResult(bool Succeeded, string Message, string? CustomerName = null, DateTimeOffset? Until = null);

public interface IGrantComplimentaryAccess
{
    Task<ComplimentaryGrantResult> ExecuteAsync(Guid customerId, int days, CancellationToken ct);
}

internal sealed class GrantComplimentaryAccess(ICustomerRepository customers) : IGrantComplimentaryAccess
{
    public async Task<ComplimentaryGrantResult> ExecuteAsync(Guid customerId, int days, CancellationToken ct)
    {
        if (days < 1)
            return new(false, "A complimentary grant needs at least 1 day.");

        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is null)
            return new(false, "No such customer.");

        var now = DateTimeOffset.UtcNow;
        try
        {
            customer.GrantComplimentary(now.AddDays(days), now);
        }
        catch (DomainException ex)
        {
            return new(false, ex.Message);
        }

        await customers.UpdateAsync(customer, ct);
        return new(true, $"Granted the full plan to {customer.Name} until {customer.ComplimentaryUntil:yyyy-MM-dd}.",
            customer.Name, customer.ComplimentaryUntil);
    }
}
```

`src/Coldstart.Application/Billing/ComplimentaryExpirySweeper.cs`:

```csharp
using Coldstart.Application.Ports;

namespace Coldstart.Application.Billing;

// Downgrades lapsed complimentary trials. endedAt: null on purpose — a comp trial
// never paid, so it must not start the privacy-policy §7 retention-deletion clock.
public sealed class ComplimentaryExpirySweeper(ICustomerRepository customers)
{
    public async Task<int> SweepAsync(DateTimeOffset now, CancellationToken ct)
    {
        var downgraded = 0;
        foreach (var customer in await customers.AllAsync(ct))
        {
            if (customer.ComplimentaryUntil is not { } until || until >= now) continue;
            if (customer.StripeSubscriptionId is not null) continue;   // converted mid-trial
            try
            {
                customer.DowngradeToFree(endedAt: null);
                await customers.UpdateAsync(customer, ct);
                downgraded++;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // One bad row must not stop the sweep; the next nightly run retries it.
            }
        }
        return downgraded;
    }
}
```

Register both where Application services are registered (find the file registering `ApplySubscriptionChange` — `grep -rn "ApplySubscriptionChange" src --include="*.cs" | grep -i inject` — and mirror its style):

```csharp
        services.AddScoped<Billing.IGrantComplimentaryAccess, Billing.GrantComplimentaryAccess>();
        services.AddScoped<Billing.ComplimentaryExpirySweeper>();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Coldstart.Application.Tests`
Expected: ALL PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Application/Billing/ tests/Coldstart.Application.Tests/GrantComplimentaryAccessTests.cs tests/Coldstart.Application.Tests/ComplimentaryExpirySweeperTests.cs
git add -u
git commit -m "feat(billing): complimentary grant use case + nightly expiry sweeper"
```

### Task 6: Nightly sweep hosted service

**Files:**
- Create: `src/Coldstart.Infrastructure/Billing/ComplimentaryExpiryService.cs`
- Modify: `src/Coldstart.Infrastructure/DependencyInjection.cs` (the `AddHarvestQueue` method that registers the other hosted services)

**Interfaces:**
- Consumes: `ComplimentaryExpirySweeper.SweepAsync(now, ct)` from Task 5; `NightlyHarvestScheduler.NextRunDelay(TimeSpan, DateTime)` (existing static helper used by `RetentionCleanupService`).
- Produces: hosted service only; nothing downstream consumes it.

- [ ] **Step 1: Implement (pattern-copy of `RetentionCleanupService`)**

`src/Coldstart.Infrastructure/Billing/ComplimentaryExpiryService.cs`:

```csharp
using Coldstart.Application.Billing;
using Coldstart.Infrastructure.Harvest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coldstart.Infrastructure.Billing;

// Fires once per day (quiet hours) and downgrades lapsed complimentary trials.
public sealed class ComplimentaryExpiryService(
    IServiceScopeFactory scopes,
    ILogger<ComplimentaryExpiryService> log) : BackgroundService
{
    private static readonly TimeSpan RunAtUtc = new(3, 0, 0);   // after the retention sweep (02:30)

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NightlyHarvestScheduler.NextRunDelay(RunAtUtc, DateTime.UtcNow);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                using var scope = scopes.CreateScope();
                var sweeper = scope.ServiceProvider.GetRequiredService<ComplimentaryExpirySweeper>();
                var count = await sweeper.SweepAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (count > 0) log.LogInformation("Complimentary sweep downgraded {Count} customer(s).", count);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Complimentary expiry sweep failed.");
            }
        }
    }
}
```

Register it in `DependencyInjection.AddHarvestQueue` next to the other hosted services:

```csharp
        services.AddHostedService<Billing.ComplimentaryExpiryService>();
```

- [ ] **Step 2: Build + full test run (the sweep logic itself is covered by Task 5's tests)**

Run: `dotnet build && dotnet test`
Expected: build clean, ALL PASS. (Timer-loop shells around tested sweepers follow the repo's existing convention of not unit-testing the `BackgroundService` shell — `RetentionCleanupService` has no shell test either.)

- [ ] **Step 3: Commit**

```bash
git add src/Coldstart.Infrastructure/Billing/ComplimentaryExpiryService.cs src/Coldstart.Infrastructure/DependencyInjection.cs
git commit -m "feat(billing): nightly hosted sweep for lapsed complimentary trials"
```

### Task 7: Grant email template + admin API endpoint + CLI verb

**Files:**
- Modify: `src/Coldstart.Web/Email/EmailTemplates.cs`
- Modify: `src/Coldstart.Web/Api/AdminEndpoints.cs`
- Modify: `src/Coldstart.Web/Api/AdminDtos.cs` (add `ComplimentaryUntil` to `AdminCustomerDto`)
- Modify: `src/Coldstart.Cli/CommandRunner.cs`
- Test: `tests/Coldstart.Web.Tests/AdminApiComplimentaryTests.cs` (new), `tests/Coldstart.Web.Tests/EmailTemplatesTests.cs`, `tests/Coldstart.Cli.Tests/` (mirror the existing CLI test file for a verb)

**Interfaces:**
- Consumes: `IGrantComplimentaryAccess` (Task 5), `IEmailSender.SendAsync(to, subject, text, html, ct)`, `EmailContent` record, `NotificationsOptions.BaseUrl`, `ColdstartDbContext.Users` (for the recipient address — same query as `EmailTrialFollowUpSender.cs:24-28`).
- Produces: `POST /api/admin/customers/{id:guid}/complimentary` body `{"days": 5}` → `200 {message, until, emailSent}` / `404` / `400`; CLI verb `comp <company|id> --days <n>`.

- [ ] **Step 1: Write the failing tests**

`tests/Coldstart.Web.Tests/AdminApiComplimentaryTests.cs` — new file. Captured mail lives on `factory.Email.Sent` (a `List<(string To, string Subject, string TextBody, string? HtmlBody)>` — see `tests/Coldstart.Web.Tests/FakeEmailSender.cs`).

**Critical arrange detail:** `AdminUserService.InviteAsync` creates the user with `EmailConfirmed = false` (`src/Coldstart.Web/Admin/AdminUserService.cs:74`); confirmation only happens when they set their password. The endpoint mails the *confirmed* account address, so a freshly invited customer gets no email. Tests must therefore confirm the user explicitly for the happy path — and the unconfirmed case is a genuine sad path worth pinning.

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public sealed class AdminApiComplimentaryTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", ColdstartWebFactory.TestAdminKey);
        return client;
    }

    private async Task<Guid> InviteAsync(HttpClient client, string email, string company)
    {
        var invite = await client.PostAsJsonAsync("/api/admin/invites", new { email, company, niche = "n" });
        return (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("customerId").GetGuid();
    }

    // Invited users start unconfirmed; the grant email only goes to a confirmed address.
    private async Task ConfirmAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        user!.EmailConfirmed = true;
        await users.UpdateAsync(user);
    }

    [Fact]
    public async Task PostComplimentary_ConfirmedCustomer_GrantsAndSendsEmail()
    {
        // Arrange
        using var client = Client();
        var email = "comp-happy@example.com";
        var customerId = await InviteAsync(client, email, "Comp Happy BV");
        await ConfirmAsync(email);
        var before = factory.Email.Sent.Count;

        // Act
        var response = await client.PostAsJsonAsync($"/api/admin/customers/{customerId}/complimentary", new { days = 5 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("emailSent").GetBoolean().Should().BeTrue();
        body.GetProperty("until").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow.AddDays(4));
        factory.Email.Sent.Skip(before).Should().Contain(m => m.To == email && m.Subject.Contains("full plan"));
    }

    [Fact]
    public async Task PostComplimentary_UnconfirmedCustomer_StillGrantsButReportsNoEmail()
    {
        // Arrange — invited, never set a password, so no confirmed address to mail
        using var client = Client();
        var customerId = await InviteAsync(client, "comp-pending@example.com", "Comp Pending BV");

        // Act
        var response = await client.PostAsJsonAsync($"/api/admin/customers/{customerId}/complimentary", new { days = 5 });

        // Assert — the grant stands; only the notification is missing
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("emailSent").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task PostComplimentary_UnknownCustomer_Returns404()
    {
        // Arrange
        using var client = Client();

        // Act
        var response = await client.PostAsJsonAsync($"/api/admin/customers/{Guid.NewGuid()}/complimentary", new { days = 5 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostComplimentary_ZeroDays_Returns400()
    {
        // Arrange
        using var client = Client();
        var customerId = await InviteAsync(client, "comp-zero@example.com", "Comp Zero BV");

        // Act
        var response = await client.PostAsJsonAsync($"/api/admin/customers/{customerId}/complimentary", new { days = 0 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostComplimentary_WithoutAdminKey_IsRejected()
    {
        // Arrange — no X-Admin-Key header
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync($"/api/admin/customers/{Guid.NewGuid()}/complimentary", new { days = 5 });

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
```

(Match the no-key expectation to whatever `AdminApiAuthTests.cs` already asserts for the other endpoints, and drop the `BeOneOf` for that exact status.)

Add to `tests/Coldstart.Web.Tests/EmailTemplatesTests.cs` (mirror that file's existing assertions style):

```csharp
[Fact]
public void ComplimentaryGranted_ContainsPlanEndDateAndAppLink()
{
    // Arrange
    var until = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

    // Act
    var content = EmailTemplates.ComplimentaryGranted("Independent", until, "https://getcoldstart.nl/app");

    // Assert
    content.Subject.Should().Contain("full plan");
    content.TextBody.Should().Contain("10 August 2026");
    content.TextBody.Should().Contain("https://getcoldstart.nl/app");
    content.TextBody.Should().Contain("No card needed");
    content.HtmlBody.Should().Contain("Independent");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "Complimentary"`
Expected: FAIL — endpoint and template don't exist.

- [ ] **Step 3: Implement the template**

In `src/Coldstart.Web/Email/EmailTemplates.cs`, add (following the `Welcome` template's shape, using the existing `Shell(...)` helper):

```csharp
    internal static EmailContent ComplimentaryGranted(string company, DateTimeOffset until, string appUrl)
    {
        var untilText = until.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        return new(
            Subject: "Thanks for the feedback — the full plan is yours for a few days",
            TextBody:
                $"Your feedback made Coldstart better, so we've switched {company} to the full plan as a thank-you.\n\n" +
                $"Until {untilText} you get everything: every lead unlocked, no daily cap, full history.\n\n" +
                "No card needed, nothing to cancel — when it ends, your account simply returns to the free plan.\n\n" +
                $"Your briefing lives here:\n\n{appUrl}\n\n" +
                "Questions or more feedback? Just reply to this email.",
            HtmlBody: Shell(
                heading: "The full plan is yours for a few days",
                introHtml: $"Your feedback made Coldstart better, so we've switched <strong>{Enc(company)}</strong> to the full plan as a thank-you. " +
                           $"Until <strong>{Enc(untilText)}</strong> you get everything: every lead unlocked, no daily cap, full history. " +
                           "No card needed, nothing to cancel — when it ends, your account simply returns to the free plan.",
                ctaText: "Open your briefing",
                ctaUrl: appUrl,
                footnote: "Questions or more feedback? Just reply to this email."));
    }
```

- [ ] **Step 4: Implement the endpoint**

In `src/Coldstart.Web/Api/AdminEndpoints.cs`, add inside `MapAdminApi` (after the invites group). Also add `public sealed record ComplimentaryRequestDto(int Days);` to `AdminDtos.cs`, and extend `AdminCustomerDto` with `DateTimeOffset? ComplimentaryUntil` (update its construction at `AdminEndpoints.cs:28-31` to pass `c.ComplimentaryUntil`):

```csharp
        group.MapPost("/customers/{id:guid}/complimentary", async (
            Guid id, ComplimentaryRequestDto body,
            Coldstart.Application.Billing.IGrantComplimentaryAccess grant,
            ColdstartDbContext db, Coldstart.Application.Ports.IEmailSender email,
            IOptions<Coldstart.Infrastructure.Notifications.NotificationsOptions> notifications,
            ILogger<Program> log, CancellationToken ct) =>
        {
            var result = await grant.ExecuteAsync(id, body.Days, ct);
            if (!result.Succeeded)
                return result.Message.Contains("No such customer")
                    ? Results.Problem(statusCode: 404, title: result.Message)
                    : Results.Problem(statusCode: 400, title: result.Message);

            // Notify the customer's confirmed account address; the grant stands even if mail fails.
            var emailSent = false;
            var to = await db.Users.IgnoreQueryFilters()
                .Where(u => u.CustomerId == id && u.Email != null && u.EmailConfirmed)
                .OrderBy(u => u.Email).Select(u => u.Email!).FirstOrDefaultAsync(ct);
            if (to is not null)
            {
                var appUrl = $"{notifications.Value.BaseUrl.TrimEnd('/')}/app";
                var content = Email.EmailTemplates.ComplimentaryGranted(result.CustomerName!, result.Until!.Value, appUrl);
                try
                {
                    await email.SendAsync(to, content.Subject, content.TextBody, content.HtmlBody, ct);
                    emailSent = true;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    log.LogError(ex, "Complimentary grant email to {Email} failed.", to);
                }
            }
            return Results.Ok(new { message = result.Message, until = result.Until, emailSent });
        });
```

Match the file's existing `using`s (add `Microsoft.EntityFrameworkCore`, `Coldstart.Infrastructure` as needed). On `db.Users`: `EmailTrialFollowUpSender.cs:24-28` runs the same query *without* `IgnoreQueryFilters()`, but it executes inside a tenant-scoped background job. This endpoint has no tenant context (admin API key, no signed-in principal), so keep `IgnoreQueryFilters()` — if the global filter applies to `Users` and no tenant is set, the unfiltered query is the only one that finds the address. The "unconfirmed customer" test above catches the wrong choice: it must return `emailSent: false` while the *confirmed* test returns `true`; if both come back `false`, the query is being filtered out and the `IgnoreQueryFilters()` call is what's missing.

Note the `EmailConfirmed` requirement is deliberate: never mail an address nobody has proven they control.

- [ ] **Step 5: Implement the CLI verb**

In `src/Coldstart.Cli/CommandRunner.cs`:

Add to the `Usage` string after the `harvest` lines:

```
  comp <company|id> --days <n>   Grant complimentary full plan for n days (emails the customer)
```

Add to the `switch` (mirror `HarvestAsync`'s company-or-id resolution — reuse its private resolver if one exists in the file, else follow the same lookup shape):

```csharp
                ["comp", var target] when GetOption(args, "--days") is { } daysRaw && int.TryParse(daysRaw, out var days) =>
                    await CompAsync(target, days, api, stdout, stderr),
                ["comp", ..] => UsageError(stderr, "comp requires <company|id> --days <n>."),
```

And the handler (model it on the existing `HarvestAsync` further down the same file — it already resolves a company name to an id via `GET /api/admin/customers`; reuse that resolution helper):

```csharp
    private static async Task<int> CompAsync(string target, int days, AdminApiClient api, TextWriter stdout, TextWriter stderr)
    {
        var id = await ResolveCustomerIdAsync(target, api);   // same helper HarvestAsync uses
        if (id is null) { await stderr.WriteLineAsync($"No customer matches '{target}'."); return 1; }
        return await ActAsync(api.PostAsync($"/api/admin/customers/{id}/complimentary", new { days }), stdout, stderr);
    }
```

(If `HarvestAsync` inlines its resolution instead of using a helper, extract that block into `ResolveCustomerIdAsync` and use it from both places.)

- [ ] **Step 6: Run all affected suites**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "Complimentary|EmailTemplates" && dotnet test tests/Coldstart.Cli.Tests`
Expected: ALL PASS. Add one CLI test mirroring the existing verb tests in `tests/Coldstart.Cli.Tests` (assert `comp Acme --days 5` posts to the resolved URL, and `comp` without `--days` prints usage) — follow that project's existing fake-API pattern exactly.

- [ ] **Step 7: Full suite + commit**

Run: `dotnet test`
Expected: ALL PASS.

```bash
git add src/Coldstart.Web/Email/EmailTemplates.cs src/Coldstart.Web/Api/ src/Coldstart.Cli/CommandRunner.cs tests/
git commit -m "feat(admin): complimentary grant endpoint + CLI verb, customer notified by email"
```

---

## Phase 3 — Sidebar shell

Approved design (mockups in `.superpowers/brainstorm/`): full-height dark rail, wordmark top, active item with amber left edge, groups with small uppercase labels, `Log out` as the bottom item in muted red with a confirmation dialog. Customer rail: Briefing · History · SETTINGS (Company, Outreach, Sources, Notifications, Subscription, Account) · Log out. Admin rail: Cockpit · Companies & invites · Mailbox · Exclusions · EXTERNAL (Visitors ↗, Stripe ↗) · Log out. Mobile: rail hides behind a hamburger in a slim top bar.

### Task 8: Rail CSS + shared confirm dialog relocation

**Files:**
- Modify: `src/Coldstart.Web/wwwroot/css/site.css`
- Move: `src/Coldstart.Web/Areas/App/Pages/Shared/_ConfirmDialog.cshtml` → `src/Coldstart.Web/Pages/Shared/_ConfirmDialog.cshtml`
- Create: `src/Coldstart.Web/wwwroot/js/app-rail.js`

**Interfaces:**
- Produces: CSS classes `.rail-shell`, `.rail`, `.rail-wordmark`, `.rail-nav`, `.rail-group`, `.rail-item`, `.rail-item.active`, `.rail-logout`, `.rail-topbar`, `.rail-burger`, `.rail-main`; partial path `/Pages/Shared/_ConfirmDialog.cshtml`; `app-rail.js` (hamburger toggle). Tasks 9–10 consume all of these.

- [ ] **Step 1: Move the confirm dialog partial**

```bash
git mv src/Coldstart.Web/Areas/App/Pages/Shared/_ConfirmDialog.cshtml src/Coldstart.Web/Pages/Shared/_ConfirmDialog.cshtml
```

Then update every reference: `grep -rn '_ConfirmDialog' src/Coldstart.Web --include="*.cshtml"` — change each `<partial name="_ConfirmDialog" />` to `<partial name="/Pages/Shared/_ConfirmDialog.cshtml" />` (full path works from any area).

- [ ] **Step 2: Add the rail CSS**

Append to `src/Coldstart.Web/wwwroot/css/site.css` (new section, before the responsive block; reuses existing custom properties `--mono`, `--ink`, `--line-soft` where sensible):

```css
/* ---- app rail (sidebar shell for /app and /admin) ---- */
.rail-shell { display: flex; min-height: 100vh; }
.rail { width: 216px; flex-shrink: 0; background: #171513; color: #f5f1ea; display: flex; flex-direction: column; padding: 18px 0; position: sticky; top: 0; height: 100vh; overflow-y: auto; }
.rail-wordmark { font-weight: 800; font-size: 1.05rem; color: #f5f1ea; text-decoration: none; padding: 0 20px 18px; letter-spacing: .02em; }
.rail-wordmark i { color: var(--sunrise, #f0a05a); font-style: normal; }
.rail-wordmark .rail-tag { font-family: var(--mono); font-size: .6rem; letter-spacing: .12em; color: #8d867a; margin-left: 6px; vertical-align: 2px; }
.rail-nav { display: flex; flex-direction: column; flex: 1; }
.rail-group { font-family: var(--mono); font-size: .62rem; letter-spacing: .14em; text-transform: uppercase; color: #8d867a; padding: 18px 20px 6px; }
.rail-item { display: block; padding: 9px 20px; color: #cfc9bf; text-decoration: none; font-size: .92rem; border-left: 3px solid transparent; }
.rail-item:hover { color: #fff; background: #221f1b; }
.rail-item.active { background: #2b2723; color: #fff; border-left-color: var(--sunrise, #f0a05a); }
.rail-logout { margin-top: auto; padding: 9px 20px; color: #e8a9a0; background: none; border: none; border-top: 1px solid #2b2723; width: 100%; text-align: left; font-size: .92rem; cursor: pointer; }
.rail-logout:hover { color: #fff; background: #221f1b; }
.rail-main { flex: 1; min-width: 0; }
.rail-topbar { display: none; }
@media (max-width: 860px) {
  .rail-shell { display: block; }
  .rail { position: fixed; inset: 0 auto 0 0; z-index: 40; transform: translateX(-100%); transition: transform .2s ease; width: 246px; }
  .rail.open { transform: none; box-shadow: 0 0 40px rgba(0,0,0,.4); }
  .rail-topbar { display: flex; align-items: center; gap: 12px; padding: 10px 16px; background: #171513; color: #f5f1ea; position: sticky; top: 0; z-index: 30; }
  .rail-topbar .rail-wordmark { padding: 0; }
  .rail-burger { background: none; border: 1px solid #3a352f; color: #f5f1ea; border-radius: 8px; padding: 6px 10px; font-size: 1rem; cursor: pointer; }
}
```

- [ ] **Step 3: Add the hamburger toggle JS**

`src/Coldstart.Web/wwwroot/js/app-rail.js`:

```js
// Mobile rail toggle. Desktop never needs JS — the rail is a sticky flex column.
(function () {
  var burger = document.getElementById("rail-burger");
  var rail = document.getElementById("app-rail");
  if (!burger || !rail) return;
  burger.addEventListener("click", function () {
    var open = rail.classList.toggle("open");
    burger.setAttribute("aria-expanded", String(open));
  });
  document.addEventListener("click", function (e) {
    if (rail.classList.contains("open") && !rail.contains(e.target) && e.target !== burger)
      rail.classList.remove("open");
  });
})();
```

- [ ] **Step 4: Build + existing tests still green**

Run: `dotnet build && dotnet test tests/Coldstart.Web.Tests`
Expected: ALL PASS (partial move must not orphan any `<partial name="_ConfirmDialog" />` reference — the Razor build catches unresolved partials only at render time, so the page tests are the real gate).

- [ ] **Step 5: Commit**

```bash
git add -A src/Coldstart.Web/wwwroot src/Coldstart.Web/Pages/Shared/_ConfirmDialog.cshtml src/Coldstart.Web/Areas
git commit -m "feat(ui): rail shell CSS, mobile toggle, confirm dialog shared across areas"
```

### Task 9: Customer panel on the rail

**Files:**
- Create: `src/Coldstart.Web/Areas/App/Pages/Shared/_AppSidebar.cshtml`
- Modify: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml`, `History.cshtml`, `Leads/Detail.cshtml`, `Billing.cshtml`, `Profile.cshtml`, `Settings/Company.cshtml`, `Settings/Outreach.cshtml`, `Settings/Sources.cshtml`, `Settings/Notifications.cshtml`, `Settings/Billing.cshtml`, `Settings/Account.cshtml`, `Settings/Index.cshtml`
- Delete: `src/Coldstart.Web/Areas/App/Pages/Settings/_SettingsNav.cshtml`, `Settings/_SettingsHeader.cshtml`
- Test: `tests/Coldstart.Web.Tests/CustomerZoneTests.cs` (add), plus existing suites as regression gate

**Interfaces:**
- Consumes: CSS/JS from Task 8; `ViewData["AppNav"]` (new convention: `"briefing" | "history" | "company" | "outreach" | "sources" | "notifications" | "billing" | "account"`); each page model's existing plan flag for the Subscription item (`Model.Plan == CustomerPlan.Full` is available on `IndexModel`; the sidebar reads a `ViewData["ShowBilling"]` bool set by pages that know the plan — default hidden).
- Produces: `_AppSidebar.cshtml` used by every App-area page.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Coldstart.Web.Tests/CustomerZoneTests.cs` (reuse its sign-in helpers):

```csharp
[Fact]
public async Task Briefing_RendersRailWithNavAndLogoutConfirm()
{
    // Arrange
    var client = factory.CreateClient();
    var email = $"rail-{Guid.NewGuid():N}@test.com";
    await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

    // Act
    var html = await client.GetStringAsync("/app");

    // Assert
    html.Should().Contain("id=\"app-rail\"");
    html.Should().Contain(">Briefing<");
    html.Should().Contain(">History<");
    html.Should().Contain("data-confirm-title=\"Log out?\"");
    html.Should().NotContain("settings-nav");   // pills are gone
}

[Fact]
public async Task Settings_ActiveRailItem_MarksCurrentSection()
{
    // Arrange
    var client = factory.CreateClient();
    var email = $"rail-set-{Guid.NewGuid():N}@test.com";
    await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

    // Act
    var html = await client.GetStringAsync("/app/settings/company");

    // Assert
    html.Should().Contain("rail-item active");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "Briefing_RendersRailWithNavAndLogoutConfirm|Settings_ActiveRailItem_MarksCurrentSection"`
Expected: FAIL.

- [ ] **Step 3: Create the sidebar partial**

`src/Coldstart.Web/Areas/App/Pages/Shared/_AppSidebar.cshtml`:

```html
@{ var nav = (string?)ViewData["AppNav"] ?? ""; var showBilling = ViewData["ShowBilling"] as bool? ?? false; }
<div class="rail-topbar">
  <button class="rail-burger" id="rail-burger" aria-label="Menu" aria-expanded="false">☰</button>
  <a class="rail-wordmark" href="/app">coldstart<i>*</i></a>
</div>
<aside class="rail" id="app-rail" aria-label="App navigation">
  <a class="rail-wordmark" href="/app">coldstart<i>*</i></a>
  <nav class="rail-nav">
    <a class="rail-item @(nav == "briefing" ? "active" : null)" href="/app">Briefing</a>
    <a class="rail-item @(nav == "history" ? "active" : null)" href="/app/history">History</a>
    <span class="rail-group">Settings</span>
    <a class="rail-item @(nav == "company" ? "active" : null)" href="/app/settings/company">Company</a>
    <a class="rail-item @(nav == "outreach" ? "active" : null)" href="/app/settings/outreach">Outreach</a>
    <a class="rail-item @(nav == "sources" ? "active" : null)" href="/app/settings/sources">Sources</a>
    <a class="rail-item @(nav == "notifications" ? "active" : null)" href="/app/settings/notifications">Notifications</a>
    @if (showBilling)
    {
      <a class="rail-item @(nav == "billing" ? "active" : null)" href="/app/settings/billing">Subscription</a>
    }
    <a class="rail-item @(nav == "account" ? "active" : null)" href="/app/settings/account">Account</a>
    <form method="post" asp-page="/Account/Logout" asp-area="Identity" style="margin-top:auto; display:contents;">
      <button class="rail-logout" type="submit"
              data-confirm="You'll need to sign in again to see your briefing."
              data-confirm-title="Log out?" data-confirm-ok="Log out">Log out</button>
    </form>
  </nav>
</aside>
```

- [ ] **Step 4: Convert every App page to the shell**

For each page listed in Files, apply the same mechanical transformation:

1. Delete the whole `<header class="app-bar">…</header>` block (and `<partial name="_SettingsHeader" />` / `<partial name="_SettingsNav" />` on settings pages).
2. Wrap the page in the shell — directly after the `@{ }` block:

```html
<div class="rail-shell">
  <partial name="_AppSidebar" />
  <div class="rail-main">
    @* existing <main class="wrap app-zone">…</main> stays exactly as it was *@
  </div>
</div>
<partial name="/Pages/Shared/_ConfirmDialog.cshtml" />
<script src="~/js/app-rail.js" defer asp-append-version="true"></script>
<script src="~/js/app-confirm.js" defer asp-append-version="true"></script>
```

  On pages that already render `_ConfirmDialog` + `app-confirm.js` (`Leads/Detail.cshtml`), do NOT double-include: keep exactly one `<partial name="/Pages/Shared/_ConfirmDialog.cshtml" />` and one `app-confirm.js` script per page.
3. Set the active item in the `@{ }` header, e.g. `ViewData["AppNav"] = "briefing";` (Index), `"history"` (History), nothing for Leads/Detail (no item active), and the section name on each settings page (replacing the old `ViewData["SettingsTab"]`). On pages whose model exposes the plan (`Index`, `Billing`, `Settings/Billing`), set `ViewData["ShowBilling"] = Model.Plan == Coldstart.Domain.CustomerPlan.Full;` — on the other pages the Subscription item simply stays hidden, which is acceptable because the pill nav had the same information asymmetry (it showed Subscription unconditionally; if you'd rather always show it, set `ShowBilling` true everywhere instead — decide once, apply consistently; recommended: always show it and let the billing page itself explain Free state, which removes per-page plan plumbing).

**Recommended resolution (apply this):** always render the Subscription item (drop `showBilling` from the partial — no `@if`), because `Settings/Billing` already renders sensibly for Free customers (it shows the upgrade pitch — verify by loading it as a Free user in the test below; if it errors instead, keep the flag mechanism).

4. Delete `Settings/_SettingsNav.cshtml` and `Settings/_SettingsHeader.cshtml` once no page references them (`grep -rn "_SettingsNav\|_SettingsHeader" src/Coldstart.Web`).

- [ ] **Step 5: Run the full Web suite**

Run: `dotnet test tests/Coldstart.Web.Tests`
Expected: ALL PASS. Pre-existing tests asserting on removed markup (e.g. anything expecting `app-bar` links or `settings-nav` on app pages) must be updated to the rail equivalents — change assertions to pin the same *behavior* (link present, target URL correct), not the old markup.

- [ ] **Step 6: Visual smoke check**

Run the app (`dotnet run --project src/Coldstart.Web`), sign in as the seeded dev user, and click through: briefing → history → a lead → each settings page → log out (confirm dialog appears, Stay keeps you signed in, Log out signs out). Check a ~400px viewport: burger opens/closes the rail.

- [ ] **Step 7: Commit**

```bash
git add -A src/Coldstart.Web/Areas/App tests/Coldstart.Web.Tests
git commit -m "feat(ui): customer panel moves from pill nav to full-height rail with confirmed logout"
```

### Task 10: Admin panel on the rail + pill retirement

**Files:**
- Create: `src/Coldstart.Web/Areas/Admin/Pages/_AdminSidebar.cshtml`
- Modify: `src/Coldstart.Web/Areas/Admin/Pages/Cockpit.cshtml`, `Index.cshtml`, `Exclusions.cshtml`, `Mailbox/Index.cshtml`, `Mailbox/Compose.cshtml`, `Mailbox/Message.cshtml`
- Delete: `src/Coldstart.Web/Areas/Admin/Pages/_AdminHeader.cshtml`
- Modify: `src/Coldstart.Web/wwwroot/css/site.css` (retire `.settings-nav` rules at lines ~741-744)
- Test: `tests/Coldstart.Web.Tests/CockpitTests.cs` (add one rail test)

**Interfaces:**
- Consumes: rail CSS/JS from Task 8; `ViewData["AdminTab"]` (existing convention, values `"cockpit" | "companies" | "mailbox" | "exclusions"`).
- Produces: `_AdminSidebar.cshtml`.

- [ ] **Step 1: Write the failing test**

Add to `tests/Coldstart.Web.Tests/CockpitTests.cs` (reuse its SuperAdmin sign-in helper):

```csharp
[Fact]
public async Task Cockpit_RendersAdminRail()
{
    // Arrange — SuperAdmin session (existing helper in this file)
    // Act
    var html = await client.GetStringAsync("/admin/cockpit");

    // Assert
    html.Should().Contain("id=\"app-rail\"");
    html.Should().Contain(">Cockpit<");
    html.Should().Contain("stats.getcoldstart.nl");
    html.Should().NotContain("settings-nav");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "Cockpit_RendersAdminRail"`
Expected: FAIL.

- [ ] **Step 3: Create the admin sidebar and convert pages**

`src/Coldstart.Web/Areas/Admin/Pages/_AdminSidebar.cshtml`:

```html
@{ var tab = (string?)ViewData["AdminTab"] ?? ""; }
<div class="rail-topbar">
  <button class="rail-burger" id="rail-burger" aria-label="Menu" aria-expanded="false">☰</button>
  <a class="rail-wordmark" href="/admin/cockpit">coldstart<i>*</i><span class="rail-tag">ADMIN</span></a>
</div>
<aside class="rail" id="app-rail" aria-label="Admin navigation">
  <a class="rail-wordmark" href="/admin/cockpit">coldstart<i>*</i><span class="rail-tag">ADMIN</span></a>
  <nav class="rail-nav">
    <a class="rail-item @(tab == "cockpit" ? "active" : null)" href="/admin/cockpit">Cockpit</a>
    <a class="rail-item @(tab == "companies" ? "active" : null)" href="/admin">Companies &amp; invites</a>
    <a class="rail-item @(tab == "mailbox" ? "active" : null)" href="/admin/mailbox">Mailbox</a>
    <a class="rail-item @(tab == "exclusions" ? "active" : null)" href="/admin/exclusions">Exclusions</a>
    <span class="rail-group">External</span>
    <a class="rail-item" href="https://stats.getcoldstart.nl" target="_blank" rel="noopener">Visitors ↗</a>
    <a class="rail-item" href="https://dashboard.stripe.com" target="_blank" rel="noopener">Stripe ↗</a>
    <form method="post" asp-page="/Account/Logout" asp-area="Identity" style="margin-top:auto; display:contents;">
      <button class="rail-logout" type="submit"
              data-confirm="You'll need to sign in again to reach the admin panel."
              data-confirm-title="Log out?" data-confirm-ok="Log out">Log out</button>
    </form>
  </nav>
</aside>
```

Convert each admin page exactly like Task 9 step 4: replace `<partial name="_AdminHeader" />` with the `rail-shell` wrapper + `_AdminSidebar` partial + `_ConfirmDialog` + `app-rail.js` + `app-confirm.js`; keep each page's existing `ViewData["AdminTab"]` assignment. Then delete `_AdminHeader.cshtml` and remove the `.settings-nav` CSS block from `site.css` (grep first: `grep -rn "settings-nav" src/` must return nothing after this task).

- [ ] **Step 4: Run the full Web suite**

Run: `dotnet test tests/Coldstart.Web.Tests`
Expected: ALL PASS (fix any admin tests pinned to the old header markup, same behavior-not-markup rule as Task 9).

- [ ] **Step 5: Visual smoke check + commit**

Sign in as SuperAdmin, click through all four admin sections + external links + log out confirm.

```bash
git add -A src/Coldstart.Web/Areas/Admin src/Coldstart.Web/wwwroot/css/site.css tests/Coldstart.Web.Tests
git commit -m "feat(ui): admin panel on the rail; pill navigation retired"
```

---

## Phase 4 — Draft variance

### Task 11: Skeleton + subject rotation and the hallucination guardrail

**Files:**
- Create: `src/Coldstart.Infrastructure/Harvest/DraftSkeletons.cs`
- Modify: `src/Coldstart.Infrastructure/Harvest/GroqLeadEnricher.cs` (`BuildPrompt`)
- Test: `tests/Coldstart.Infrastructure.Tests/DraftSkeletonsTests.cs` (new), `tests/Coldstart.Infrastructure.Tests/GroqLeadEnricherTests.cs` (extend)

**Interfaces:**
- Consumes: `RawItem` (`Fingerprint`, `Url`, `Author` — key material), `SenderContext.HasFixedPitch`.
- Produces: `internal static class DraftSkeletons` with:
  - `internal static int IndexFor(string key, int count)` — stable FNV-1a hash mod count (never `string.GetHashCode()`, which is per-process randomized).
  - `internal static string SubjectInstruction(string key)` — one of 4 subject patterns.
  - `internal static string NormalDraftInstruction(string key, NicheOptions n)` — one of 5 skeletons.
  - `internal static string FixedPitchDraftInstruction(string key)` — one of 3 opener/analysis variants.

- [ ] **Step 1: Write the failing tests**

`tests/Coldstart.Infrastructure.Tests/DraftSkeletonsTests.cs`:

```csharp
using Coldstart.Application.Harvest;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class DraftSkeletonsTests
{
    [Fact]
    public void IndexFor_SameKey_IsDeterministic()
    {
        // Arrange / Act / Assert — stable across calls (and across processes, by construction)
        DraftSkeletons.IndexFor("lead-abc", 5).Should().Be(DraftSkeletons.IndexFor("lead-abc", 5));
    }

    [Fact]
    public void IndexFor_ManyKeys_CoversAllVariants()
    {
        // Arrange
        var keys = Enumerable.Range(0, 200).Select(i => $"lead-{i}");

        // Act
        var hit = keys.Select(k => DraftSkeletons.IndexFor(k, 5)).Distinct().OrderBy(x => x).ToList();

        // Assert — 200 leads must spread over all 5 skeletons
        hit.Should().BeEquivalentTo([0, 1, 2, 3, 4]);
    }

    [Fact]
    public void IndexFor_EmptyKey_StillReturnsValidIndex()
    {
        // Arrange / Act
        var index = DraftSkeletons.IndexFor("", 5);

        // Assert
        index.Should().BeInRange(0, 4);
    }

    [Fact]
    public void SubjectInstruction_DifferentKeys_ProduceDifferentPatterns()
    {
        // Arrange
        var keys = Enumerable.Range(0, 50).Select(i => $"lead-{i}");

        // Act
        var patterns = keys.Select(DraftSkeletons.SubjectInstruction).Distinct().Count();

        // Assert
        patterns.Should().BeGreaterThan(1);
    }

    [Fact]
    public void NormalDraftInstruction_AllVariants_KeepSharedInvariants()
    {
        // Arrange
        var niche = new NicheOptions { SenderName = "Ann", SenderTitle = "Founder" };

        // Act — sample enough keys to hit all 5 variants
        var variants = Enumerable.Range(0, 200)
            .Select(i => DraftSkeletons.NormalDraftInstruction($"lead-{i}", niche))
            .Distinct().ToList();

        // Assert — every skeleton keeps greeting rule, honesty and banned list
        variants.Should().HaveCount(5);
        variants.Should().AllSatisfy(v =>
        {
            v.Should().Contain("Hi there,");
            v.Should().Contain("Never guess or invent a name");
            v.Should().Contain("Banned:");
        });
    }

    [Fact]
    public void FixedPitchDraftInstruction_AllVariants_KeepQuotedAnalysisAndHonesty()
    {
        // Act
        var variants = Enumerable.Range(0, 200)
            .Select(i => DraftSkeletons.FixedPitchDraftInstruction($"lead-{i}"))
            .Distinct().ToList();

        // Assert
        variants.Should().HaveCount(3);
        variants.Should().AllSatisfy(v =>
        {
            v.Should().Contain("> ");                       // quoted analysis stays
            v.Should().Contain("do NOT sell");              // appended-pitch contract stays
            v.Should().Contain("never claim leads are already found");
        });
    }
}
```

Extend `tests/Coldstart.Infrastructure.Tests/GroqLeadEnricherTests.cs` — first open the file and reuse its existing fake-HTTP + prompt-capture pattern (it already asserts prompt content for skip feedback etc.). Add:

```csharp
[Fact]
public async Task EnrichAsync_DifferentLeads_GetDifferentDraftSkeletons()
{
    // Arrange — two items with different fingerprints, capture both outgoing prompts
    // (reuse this file's existing capturing HttpMessageHandler)

    // Act — enrich item A and item B

    // Assert — pick two fingerprints whose IndexFor differ (compute in-test via
    // DraftSkeletons.IndexFor) and assert the two captured prompts' DRAFT sections differ.
    promptA.Should().NotBe(promptB);
}

[Fact]
public async Task EnrichAsync_Prompt_NeverInvitesInventingTheirMarket()
{
    // Arrange — fixed-pitch sender (reuse existing fixture)

    // Act — capture the prompt

    // Assert — the hallucination guardrail replaced the invention instruction
    prompt.Should().NotContain("Invent it from THEIR market");
    prompt.Should().Contain("only when the harvested content itself supports");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "DraftSkeletons|EnrichAsync_DifferentLeads|EnrichAsync_Prompt_Never"`
Expected: FAIL — `DraftSkeletons` doesn't exist.

- [ ] **Step 3: Implement `DraftSkeletons`**

`src/Coldstart.Infrastructure/Harvest/DraftSkeletons.cs`:

```csharp
using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

// Structural variance for generated drafts. One fixed prompt skeleton made every
// email share the same sentence shape ("Congrats on raising $X… you're likely under
// pressure to…") — a pattern spam filters increasingly flag across sends. Each lead
// deterministically gets one of several genuinely different skeletons (and subject
// patterns), so a night's batch never shares one shape. Selection must be stable
// across processes: FNV-1a, never string.GetHashCode().
internal static class DraftSkeletons
{
    // Shared invariants — every skeleton embeds all three verbatim.
    private const string Greeting =
        "- Greeting on its own line: \"Hi <first name>,\" when the author's real first name is clearly visible in the content; otherwise exactly \"Hi there,\". Never guess or invent a name.";
    private const string Banned =
        "Banned: \"I hope this email finds you well\", \"I came across\", \"reaching out\", \"game-changer\", \"revolutionize\", \"synergy\", \"circle back\", \"touch base\", \"in today's fast-paced\".";
    private const string Honesty =
        "STRICTLY honest: never invent facts, numbers, names, or results — never claim leads are already found.";
    // The hallucination guardrail (replaces "Invent it from THEIR market").
    internal const string EvidenceRule =
        "Name specific decision-makers, segments, or companies only when the harvested content itself supports " +
        "them; if their market is not clear from the content, stay general (\"the customers you're targeting\") — " +
        "never invent an industry for them, and never reuse an example from these instructions.";

    internal static int IndexFor(string key, int count)
    {
        // FNV-1a 32-bit — stable across runs and machines.
        uint hash = 2166136261;
        foreach (var c in key) { hash ^= c; hash *= 16777619; }
        return (int)(hash % (uint)count);
    }

    private static readonly string[] SubjectPatterns =
    [
        "personal and specific to their moment, following the pattern \"<their moment> + an idea for <their company or name>\" (e.g. \"Congrats on the raise + an idea for Acme\"). Always include their company or name.",
        "a short, concrete question about the pressure their moment creates, naming their company (e.g. \"Scaling sales at Acme after the raise?\"). No greeting words in the subject.",
        "their moment stated plainly as news they recognize, plus a hint of the idea (e.g. \"Acme's Series A — one thought on what comes next\").",
        "lowercase, casual, as if a colleague wrote it, naming the company (e.g. \"an idea for acme's next quarter\"). No exclamation marks.",
    ];

    internal static string SubjectInstruction(string key) =>
        $"SUBJECT: {SubjectPatterns[IndexFor(key, SubjectPatterns.Length)]} Max 10 words, no clickbait, no emojis.";

    // Five genuinely different email shapes. Each defines the paragraph structure only;
    // greeting/honesty/banned rules are shared invariants.
    private static readonly Func<NicheOptions, string>[] NormalShapes =
    [
        n => $"""
        DRAFT: a short, warm email from {n.SenderName} ({n.SenderTitle}) — 5-7 short sentences: greeting line, then 2-3 paragraphs separated by ONE blank line:
        {Greeting}
        - Paragraph 1 (observation-first): open with the concrete thing they did or published — the specific fact, no praise words — then the need it creates for them right now.
        - Paragraph 2: the specific outcome the offer gives them for that need — a result they'd want, not a feature list.
        - Final paragraph: one friendly, low-effort question that is easy to answer with a single sentence. If the OFFER includes anything free or no-commitment, the question must offer exactly that.
        {Honesty} {EvidenceRule}
        Short, human, no marketing voice. {Banned}
        """,
        n => $"""
        DRAFT: a short, curious email from {n.SenderName} ({n.SenderTitle}) — 4-6 short sentences: greeting line, then 2 paragraphs separated by ONE blank line:
        {Greeting}
        - Paragraph 1 (question-first): open with one sharp, genuine question about how they're handling the concrete challenge their moment creates — proof you read their content, not flattery.
        - Paragraph 2: one sentence on the outcome the offer gives them for exactly that, then one friendly, low-effort closing question. If the OFFER includes anything free or no-commitment, offer exactly that.
        {Honesty} {EvidenceRule}
        Short, human, no marketing voice. {Banned}
        """,
        n => $"""
        DRAFT: a short, direct email from {n.SenderName} ({n.SenderTitle}) — 5-7 short sentences: greeting line, then 2-3 paragraphs separated by ONE blank line:
        {Greeting}
        - Paragraph 1 (pressure-first): name the clock that just started ticking for them — what their moment demands they get right in the next few months — grounded in the concrete fact from their content.
        - Paragraph 2: how the offer takes exactly that pressure off — one outcome, stated plainly.
        - Final paragraph: one friendly, low-effort question. If the OFFER includes anything free or no-commitment, offer exactly that.
        {Honesty} {EvidenceRule}
        Short, human, no marketing voice. {Banned}
        """,
        n => $"""
        DRAFT: an ultra-short note from {n.SenderName} ({n.SenderTitle}) — at most 4 sentences total, greeting line plus ONE paragraph:
        {Greeting}
        - The paragraph: one sentence tying their concrete moment to the need it creates, one sentence on the outcome the offer gives them, one friendly, low-effort question. If the OFFER includes anything free or no-commitment, offer exactly that.
        {Honesty} {EvidenceRule}
        Reads like a busy peer typed it in 40 seconds. {Banned}
        """,
        n => $"""
        DRAFT: a short, warm email from {n.SenderName} ({n.SenderTitle}) — 5-7 short sentences: greeting line, then 2-3 paragraphs separated by ONE blank line:
        {Greeting}
        - Paragraph 1 (outcome-first): open with the result they're presumably chasing right now — stated as their goal, grounded in the concrete fact from their content — never with praise.
        - Paragraph 2: the shortest honest path the offer gives them to that result.
        - Final paragraph: one friendly, low-effort question. If the OFFER includes anything free or no-commitment, offer exactly that.
        {Honesty} {EvidenceRule}
        Short, human, no marketing voice. {Banned}
        """,
    ];

    internal static string NormalDraftInstruction(string key, NicheOptions n) =>
        NormalShapes[IndexFor(key, NormalShapes.Length)](n);

    // Fixed-pitch mode: the sender's intro/pitch/CTA are appended after the AI text,
    // so every variant keeps the contract: no selling, no offer names, no closing question,
    // and a "> "-quoted analysis block (DraftComposer anchors the intro before it).
    private static readonly string[] FixedPitchShapes =
    [
        $"""
        DRAFT: ONLY the greeting, one congratulations sentence, and a short quoted analysis — the sender's intro, pitch and call-to-action are appended automatically after your text, so do NOT sell, do NOT name any offer or tool, do NOT ask a closing question. Produce exactly:
        {Greeting}
        - After a blank line: ONE short sentence — a genuine, specific congratulations on their moment, naming the concrete thing. Speak TO them (always you/your, never their company as a third-person subject). Warm; an exclamation mark is fine.
        - After a blank line: the quoted analysis — at most 2 short sentences where EVERY line starts with "> ". Name the pressure this moment creates for them and who a lead engine could find for them to connect with. {EvidenceRule}
        {Honesty}
        Short, human, no marketing voice. {Banned}
        """,
        $"""
        DRAFT: ONLY the greeting, one observation sentence, and a short quoted analysis — the sender's intro, pitch and call-to-action are appended automatically after your text, so do NOT sell, do NOT name any offer or tool, do NOT ask a closing question. Produce exactly:
        {Greeting}
        - After a blank line: ONE short sentence stating the concrete thing they just did, plainly and specifically — recognition, not praise. Speak TO them (always you/your).
        - After a blank line: the quoted analysis — at most 2 short sentences where EVERY line starts with "> ". Name what this moment means for their pipeline and who a lead engine could find for them. {EvidenceRule}
        {Honesty}
        Short, human, no marketing voice. {Banned}
        """,
        $"""
        DRAFT: ONLY the greeting, one forward-looking sentence, and a short quoted analysis — the sender's intro, pitch and call-to-action are appended automatically after your text, so do NOT sell, do NOT name any offer or tool, do NOT ask a closing question. Produce exactly:
        {Greeting}
        - After a blank line: ONE short sentence on what their moment sets them up to do next — their opportunity, stated to them (always you/your), grounded in the concrete fact. No congratulations word needed.
        - After a blank line: the quoted analysis — at most 2 short sentences where EVERY line starts with "> ". Name the first customers they'll need for that next step and how a lead engine could surface them. {EvidenceRule}
        {Honesty}
        Short, human, no marketing voice. {Banned}
        """,
    ];

    internal static string FixedPitchDraftInstruction(string key) =>
        FixedPitchShapes[IndexFor(key, FixedPitchShapes.Length)];
}
```

- [ ] **Step 4: Wire into `BuildPrompt`**

In `GroqLeadEnricher.BuildPrompt` (line 108):

1. Compute the rotation key at the top of the method:

```csharp
        var key = item.Fingerprint;   // SHA-256 hex of "{Source}:{ExternalId}" — stable per item, never empty
```

`RawItem` is `RawItem(string Source, string ExternalId, string Title, string Text, string Author, string Url, string CreatedAt)` with a computed `Fingerprint` property (`src/Coldstart.Application/Harvest/RawItem.cs`), so no fallback is needed — it is always a 64-char hex string. Note this also means the same lead always gets the same skeleton across re-runs, which is what makes the behavior testable.

2. Replace the hard-coded `SUBJECT:` line with `{DraftSkeletons.SubjectInstruction(key)}`.
3. Replace the entire `sender.HasFixedPitch ? $""" DRAFT: … """ : $""" DRAFT: … """` conditional block with:

```csharp
        {(sender.HasFixedPitch ? DraftSkeletons.FixedPitchDraftInstruction(key) : DraftSkeletons.NormalDraftInstruction(key, n))}
```

`BuildPrompt` becomes non-static-friendly only if needed — keep it `private static` (all inputs are parameters). The MentionSourceTool trailer block at the end of the prompt stays as is.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests`
Expected: ALL PASS — including all pre-existing `GroqLeadEnricherTests`; any old test pinned to the removed "an idea for" subject phrasing or the old DRAFT wording must be updated to assert the new invariants (greeting rule, banned list, honesty rule present) rather than one skeleton's exact text.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Infrastructure/Harvest/DraftSkeletons.cs src/Coldstart.Infrastructure/Harvest/GroqLeadEnricher.cs tests/Coldstart.Infrastructure.Tests/
git commit -m "feat(drafts): per-lead skeleton + subject rotation; evidence-only guardrail replaces market invention"
```

### Task 12: Secondary context lookup (second data point)

**Files:**
- Create: `src/Coldstart.Application/Harvest/ISecondaryContextLookup.cs`
- Create: `src/Coldstart.Infrastructure/Harvest/LangSearchSecondaryLookup.cs`
- Modify: `src/Coldstart.Application/Harvest/ILeadEnricher.cs` (+ its implementations and call site)
- Modify: `src/Coldstart.Application/Harvest/HarvestLeads.cs:71-73`
- Modify: `src/Coldstart.Infrastructure/Harvest/GroqLeadEnricher.cs` (prompt section)
- Modify: DI registration file (where `ILeadEnricher` is registered) + `NicheOptions.LangSearch` options class (add `SecondaryContextEnabled`)
- Test: `tests/Coldstart.Application.Tests/HarvestLeadsTests.cs` (extend existing), `tests/Coldstart.Infrastructure.Tests/LangSearchSecondaryLookupTests.cs` (new), `GroqLeadEnricherTests.cs` (extend)

**Interfaces:**
- Produces:
  - `ISecondaryContextLookup { Task<string?> LookupAsync(string company, CancellationToken ct); }` (Application port).
  - `ILeadEnricher.EnrichAsync(RawItem item, NicheOptions niche, SignalResult signal, SenderContext sender, string? secondaryContext, CancellationToken ct)` — signature gains `secondaryContext` before `ct`.
  - `LangSearchOptions.SecondaryContextEnabled` (bool, default `true`) — the off switch.

- [ ] **Step 1: Write the failing tests**

`tests/Coldstart.Infrastructure.Tests/LangSearchSecondaryLookupTests.cs` (reuse the fake-`HttpMessageHandler` helper pattern from `LangSearchSourceTests.cs` — open it first and copy its fixture shape):

```csharp
[Fact]
public async Task LookupAsync_HitFound_ReturnsSnippetWithHonestyPreamble()
{
    // Arrange — handler returns one webPages hit for "Ellis AI announcement OR blog OR interview"
    // Act
    var context = await sut.LookupAsync("Ellis AI", CancellationToken.None);

    // Assert
    context.Should().NotBeNull();
    context.Should().Contain("publication date unknown");   // honesty preamble
    context.Should().Contain("snippet text from the fake response");
}

[Fact]
public async Task LookupAsync_ApiFails_ReturnsNull()
{
    // Arrange — handler returns 500
    // Act
    var context = await sut.LookupAsync("Ellis AI", CancellationToken.None);
    // Assert
    context.Should().BeNull();
}

[Fact]
public async Task LookupAsync_DisabledOrNoKeyOrBlankCompany_ReturnsNull()
{
    // Three arrange variants: SecondaryContextEnabled=false / ApiKey="" / company "  "
    // Each must return null without any HTTP call (assert handler was never invoked).
}
```

Extend `tests/Coldstart.Application.Tests/HarvestLeadsTests.cs` (open it first; it already substitutes `ILeadEnricher` — update all its existing `EnrichAsync` stubs/verifications for the new parameter):

```csharp
[Fact]
public async Task RunForCustomerAsync_LookupSucceeds_PassesSecondaryContextToEnricher()
{
    // Arrange — ISecondaryContextLookup substitute returns "extra snippet" for the item's author
    // Act — run the pipeline
    // Assert
    await enricher.Received().EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(),
        Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), "extra snippet", Arg.Any<CancellationToken>());
}

[Fact]
public async Task RunForCustomerAsync_LookupThrows_EnrichesWithNullContext()
{
    // Arrange — lookup substitute throws
    // Act — run the pipeline
    // Assert — the batch is not aborted; enricher called with null secondary context
    await enricher.Received().EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(),
        Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), null, Arg.Any<CancellationToken>());
}
```

Extend `GroqLeadEnricherTests.cs`:

```csharp
[Fact]
public async Task EnrichAsync_WithSecondaryContext_PromptContainsSecondaryContextSection()
{
    // Arrange / Act — capture prompt with secondaryContext: "They just hired a VP Sales."
    // Assert
    prompt.Should().Contain("SECONDARY CONTEXT");
    prompt.Should().Contain("They just hired a VP Sales.");
}

[Fact]
public async Task EnrichAsync_NullSecondaryContext_PromptOmitsSection()
{
    // Assert
    prompt.Should().NotContain("SECONDARY CONTEXT");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "SecondaryContext|LangSearchSecondaryLookup"`
Expected: FAIL — compile errors.

- [ ] **Step 3: Implement**

`src/Coldstart.Application/Harvest/ISecondaryContextLookup.cs`:

```csharp
namespace Coldstart.Application.Harvest;

// One extra public snippet about a company, fetched before enrichment so the draft
// has a second data point beyond the harvested item. Null when unavailable — the
// pipeline must always proceed without it.
public interface ISecondaryContextLookup
{
    Task<string?> LookupAsync(string company, CancellationToken ct);
}
```

`src/Coldstart.Infrastructure/Harvest/LangSearchSecondaryLookup.cs` (reuse `LangSearchSource`'s request shape and its honest-framing lesson — dates are unreliable, never claim recency):

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

// Fetches one extra public snippet about the company (own announcement, founder quote)
// via the free LangSearch API. Best-effort: any failure, missing key, or the config
// off-switch yields null and the pipeline proceeds on the primary item alone.
internal sealed class LangSearchSecondaryLookup(HttpClient http, NicheOptions niche) : ISecondaryContextLookup
{
    internal const string Preamble =
        "SECONDARY CONTEXT (an additional public snippet about the same company; " +
        "publication date unknown — background evidence only, same rules: never treat " +
        "it as current news, never invent beyond it):";

    public async Task<string?> LookupAsync(string company, CancellationToken ct)
    {
        var o = niche.LangSearch;
        if (!o.SecondaryContextEnabled || string.IsNullOrWhiteSpace(o.ApiKey) || string.IsNullOrWhiteSpace(company))
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.langsearch.com/v1/web-search")
        {
            Content = JsonContent.Create(new { query = $"\"{company.Trim()}\" announcement OR blog OR interview", count = 3 }),
        };
        request.Headers.Add("Authorization", $"Bearer {o.ApiKey}");
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (!root.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("webPages", out var pages) ||
                !pages.TryGetProperty("value", out var hits) || hits.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var hit in hits.EnumerateArray())
            {
                var name = hit.TryGetProperty("name", out var n) ? n.GetString() : null;
                var snippet = hit.TryGetProperty("snippet", out var s) ? s.GetString() : null;
                if (string.IsNullOrWhiteSpace(snippet)) continue;
                return $"{Preamble}\n{name}: {snippet}".Trim();
            }
            return null;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;   // best-effort by contract
        }
    }
}
```

`ILeadEnricher` + `GroqLeadEnricher.EnrichAsync` + `BuildPrompt`: add the `string? secondaryContext` parameter (before `ct`); in `BuildPrompt` insert after the HARVESTED CONTENT block:

```csharp
        {(string.IsNullOrWhiteSpace(secondaryContext) ? "" : $"""

        {secondaryContext}
        """)}
```

`HarvestLeads`: inject `ISecondaryContextLookup lookup` in the primary constructor; in Phase 2 of `RunPipelineAsync` (line 71), before `enricher.EnrichAsync`:

```csharp
            string? secondary = null;
            try { secondary = await lookup.LookupAsync(item.Author, ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { /* best-effort */ }
            var result = await enricher.EnrichAsync(item, runNiche, signal, sender, secondary, ct);
```

(`item.Author` carries the company/author name pre-enrichment — the best key available before the LLM extracts the real company. Blank author → the lookup returns null by contract.)

Options: add to the `LangSearchOptions` class (find it: `grep -rn "class LangSearchOptions" src`):

```csharp
    public bool SecondaryContextEnabled { get; set; } = true;
```

DI: register next to `ILeadEnricher`'s registration (same file/method):

```csharp
        services.AddScoped<Application.Harvest.ISecondaryContextLookup, Harvest.LangSearchSecondaryLookup>();
```

(match the enricher's exact lifetime + HttpClient registration style — if `GroqLeadEnricher`/`LangSearchSource` use `AddHttpClient<…>`, mirror that.)

Update every existing `ILeadEnricher` substitute/fake in tests for the new parameter.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: ALL PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Application/Harvest/ src/Coldstart.Infrastructure/Harvest/ tests/
git add -u
git commit -m "feat(drafts): optional LangSearch secondary context gives every draft a second data point"
```

---

## Phase 5 — Ship it

### Task 13: Full verification pass

- [ ] **Step 1: Full suite, zero warnings tolerated in changed code**

Run: `dotnet build -warnaserror && dotnet test`
Expected: build clean, ALL PASS (expect ~360+ tests: the pre-existing 344 plus this plan's additions).

- [ ] **Step 2: Manual smoke (local)**

`dotnet run --project src/Coldstart.Web`, then walk: briefing rail → history → skipped lead reopen → detail baseline label → settings pages → admin rail (SuperAdmin) → log out confirms in both panels. On a narrow window: burger works in both panels.

- [ ] **Step 3: Commit anything outstanding; hand to the operator**

The operator (not the agent) pushes and deploys — per repo convention the agent never runs `git push`.

### Task 14: Rollout + the tester's grant (operator-assisted)

- [ ] **Step 1: Deploy** — operator pushes `main`; on the server (`ssh coldstart-prod`, `/opt/coldstart`): pull + `docker compose -f docker-compose.prod.yml up -d --build`. Migrations apply on startup (verify by checking the app logs for the `AddCustomerComplimentaryUntil` migration line; if the repo applies migrations differently, follow `deploy/` docs).
- [ ] **Step 2: Identify the tester** — `coldstart customers` (CLI) and confirm the customer named "Independent" and the account email with the operator before granting.
- [ ] **Step 3: Grant** — `coldstart comp Independent --days 5`. Verify the response reports `emailSent: true` and the plan flips via `coldstart customers`.
- [ ] **Step 4: Personal reply** — operator sends the prepared feedback reply (thanks + honest scoring answer) from the admin Mailbox. Draft text is delivered alongside this plan, not part of the codebase.

## Self-Review Notes

- Spec coverage: reopen fix (T1), scoring label (T2), complimentary domain/EF/use-case/sweep/endpoint/email/CLI (T3–T7), sidebar both panels + confirm-logout + pill retirement (T8–T10), skeleton+subject rotation + guardrail (T11), secondary context with off switch (T12), rollout + grant + personal reply (T13–T14). Error-handling section of the spec: 404/400 in T7, email-failure-still-grants in T7, lookup-never-blocks in T12, per-row sweep isolation in T5.
- Type consistency: `GrantComplimentary(until, now)` (T3) ↔ use case (T5); `ComplimentaryExpirySweeper.SweepAsync(now, ct)` (T5) ↔ hosted service (T6); `IGrantComplimentaryAccess.ExecuteAsync(Guid, int, ct)` (T5) ↔ endpoint (T7); rail CSS ids/classes (T8) ↔ partials (T9, T10); `DraftSkeletons.IndexFor/SubjectInstruction/NormalDraftInstruction/FixedPitchDraftInstruction` (T11) ↔ `BuildPrompt` wiring; `ILeadEnricher.EnrichAsync(…, string? secondaryContext, ct)` (T12) ↔ `HarvestLeads` call.
- Verified against the real code while writing this plan: `factory.Email.Sent` is the captured-mail accessor (`FakeEmailSender`); invited users are `EmailConfirmed = false` until they set a password, so the grant email needs a confirmed address (T7 covers both paths); `RawItem.Fingerprint` is computed and never empty (T11 key); plan/subscription domain tests live in `CustomerBillingTests.cs`; `tests/Coldstart.Application.Tests/HarvestLeadsTests.cs` already exists and substitutes `ILeadEnricher`, so T12's signature change touches it.
- Known judgment calls the implementer may still hit: exact rendered form of `asp-page-handler` in assertions (T1 note), the no-admin-key status code (T7 — match `AdminApiAuthTests`), Subscription item visibility (T9 — recommended: always show).

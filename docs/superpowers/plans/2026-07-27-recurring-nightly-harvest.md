# Recurring Nightly Harvest — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically top up each Full-plan customer with fresh leads every night, so the product delivers its "wake up to fresh leads every morning" promise.

**Architecture:** A new `BackgroundService` fires once nightly (~04:00 UTC), asks a new repository query for the customers that are *due* (Full plan, not mid-harvest, fewer than 5 unreviewed leads), and enqueues a `HarvestJob` for each onto the **existing** `HarvestQueue`. The existing `HarvestQueueService` drains them unchanged. No change to the harvest pipeline.

**Tech Stack:** .NET 10, EF Core / Npgsql, `Microsoft.Extensions.Hosting` `BackgroundService`, `IOptions`. Tests: xUnit + FluentAssertions against `ColdstartWebFactory` (real Postgres via Testcontainers).

**Design source of truth:** `docs/superpowers/specs/2026-07-27-recurring-nightly-harvest-design.md`

## Global Constraints

- **Top-up policy:** a customer is *due* when ALL hold — `Plan == CustomerPlan.Full`, `HarvestStatus != HarvestStatus.Running`, and count of leads with `Status in (PendingReview, Approved)` `< TopUpThreshold` (default **5**). "Unfinished" = PendingReview + Approved (same definition as the briefing/`LeadBriefing`).
- **Reuse, don't rebuild:** do NOT modify `HarvestLeads`, `HarvestQueue`, `HarvestQueueService`, `HarvestJob`, or the dedup/scoring/enrichment. This feature only adds a trigger + a policy query.
- **Cadence:** one run per day at `RunAtUtc` (default `04:00:00`). No catch-up for a missed run.
- **Cross-customer reads are system-level** (no HTTP tenant context) → use `IgnoreQueryFilters()`, exactly as `CustomerRepository.AllAsync`/`GetByNameAsync`/`DeleteAsync` already do.
- **EF value-object translation gotcha (seen in prior work):** inside an EF `Where`/`Select`, `x.CustomerId.Value == guid` and projecting `.Id.Value` may NOT translate. Value-object *equality* (`l.CustomerId == c.Id`) DOES translate. When you need the raw `Guid` out, materialize the filtered entities first and map `.Id.Value` in memory, or verify translation against real Postgres.
- **C#/.NET standards:** ports in Application, impls `internal`/`sealed` in Infrastructure; no `IQueryable<T>` crossing to Application (return `IReadOnlyList<Guid>`); `CancellationToken` on all IO; async all the way.
- **Tests:** xUnit + FluentAssertions, AAA with comments, naming `Method_State_Expected`. Seed via `factory.Services.CreateScope()` like `CustomerZoneTests`.
- **Git:** never `git push`; never add a `Co-Authored-By` trailer. Commit after each task.

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `src/Coldstart.Application/Ports/ICustomerRepository.cs` | Add `IdsDueForHarvestAsync(int threshold, CancellationToken)` | 1 |
| `src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs` | Implement the due-customers query (EF, `IgnoreQueryFilters`) | 1 |
| `tests/Coldstart.Web.Tests/NightlyHarvestTests.cs` (new) | Policy-query matrix test; enqueue-step test; NextRunDelay unit test | 1, 2 |
| `src/Coldstart.Infrastructure/Harvest/HarvestScheduleOptions.cs` (new) | `Enabled` / `RunAtUtc` / `TopUpThreshold` | 2 |
| `src/Coldstart.Infrastructure/Harvest/NightlyHarvestScheduler.cs` (new) | `BackgroundService` + testable `EnqueueDueAsync` + pure `NextRunDelay` | 2 |
| `src/Coldstart.Infrastructure/DependencyInjection.cs` | Register the hosted service in `AddHarvestQueue` | 2 |
| `src/Coldstart.Web/Program.cs` | Bind `HarvestScheduleOptions` from config | 2 |
| `src/Coldstart.Web/appsettings.json` | Document the `HarvestSchedule` section (defaults) | 2 |
| `tests/Coldstart.Web.Tests/ColdstartWebFactory.cs` | Disable the scheduler in tests (`HarvestSchedule:Enabled=false`) | 2 |

---

## Task 1: `IdsDueForHarvestAsync` — the top-up policy query

**Files:**
- Modify: `src/Coldstart.Application/Ports/ICustomerRepository.cs`
- Modify: `src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs`
- Create: `tests/Coldstart.Web.Tests/NightlyHarvestTests.cs`

**Interfaces:**
- Produces: `Task<IReadOnlyList<Guid>> ICustomerRepository.IdsDueForHarvestAsync(int threshold, CancellationToken ct)` — ids of Full-plan, not-`Running` customers whose unfinished-lead count `< threshold`.
- Consumes: `Customer.Plan` (`CustomerPlan`), `Customer.HarvestStatus` (`HarvestStatus`), `Lead.Status` (`LeadStatus`), `Lead.CustomerId` (`CustomerId`).

- [ ] **Step 1: Write the failing policy-query test**

Create `tests/Coldstart.Web.Tests/NightlyHarvestTests.cs`. Seed the full matrix and assert the returned id set is exactly the due ones. Seed customers + leads via a service scope (mirror `CustomerZoneTests`/`LeadReviewTests` helpers — use `Lead.CreateHarvested(...)` and set status via the domain; set plan via `Customer.SetPlan(CustomerPlan.Full)`; set `HarvestStatus` via `Customer.MarkHarvest(HarvestStatus.Running, null)`).

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public class NightlyHarvestTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task IdsDueForHarvest_ReturnsOnlyFull_NotRunning_BelowThreshold()
    {
        // Arrange — one customer per row of the policy matrix (threshold = 5).
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();

        var dueLow = await SeedAsync(db, "due-low", CustomerPlan.Full, HarvestStatus.Done, pending: 4);   // due
        var dueEmpty = await SeedAsync(db, "due-empty", CustomerPlan.Full, HarvestStatus.None, pending: 0); // due
        var dueOnlySent = await SeedAsync(db, "due-sent", CustomerPlan.Full, HarvestStatus.Done, pending: 0, sent: 3); // due (0 unfinished)
        await SeedAsync(db, "full-atcap", CustomerPlan.Full, HarvestStatus.Done, pending: 5);              // skip (== threshold)
        await SeedAsync(db, "full-over", CustomerPlan.Full, HarvestStatus.Done, pending: 15);              // skip
        await SeedAsync(db, "full-running", CustomerPlan.Full, HarvestStatus.Running, pending: 0);         // skip (in-flight)
        await SeedAsync(db, "free-low", CustomerPlan.Free, HarvestStatus.Done, pending: 0);                // skip (free)

        // Act
        var due = await repo.IdsDueForHarvestAsync(5, default);

        // Assert
        due.Should().BeEquivalentTo(new[] { dueLow, dueEmpty, dueOnlySent });
    }

    // Approved-but-not-sent counts as unfinished too (same as the briefing), so it gates like Pending.
    [Fact]
    public async Task IdsDueForHarvest_CountsApprovedAsUnfinished()
    {
        // Arrange
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var id = await SeedAsync(db, "appr", CustomerPlan.Full, HarvestStatus.Done, pending: 0, approved: 5); // 5 unfinished

        // Act
        var due = await repo.IdsDueForHarvestAsync(5, default);

        // Assert
        due.Should().NotContain(id);
    }

    // Seeds a customer (plan + harvest status) and the requested number of leads in each status.
    private static async Task<Guid> SeedAsync(ColdstartDbContext db, string name, CustomerPlan plan,
        HarvestStatus status, int pending = 0, int approved = 0, int sent = 0)
    {
        var customer = Customer.Create(name);              // use the real factory; adjust if the signature differs
        customer.SetPlan(plan);
        customer.MarkHarvest(status, status == HarvestStatus.Running ? null : DateTimeOffset.UtcNow);
        db.Customers.Add(customer);
        void AddLeads(int n, Action<Lead> mutate)
        {
            for (var i = 0; i < n; i++)
            {
                var lead = Lead.CreateHarvested(customer.Id, $"{name}-co-{Guid.NewGuid():N}", Fit.Yes,
                    "subj", "draft", "src", $"https://x/{Guid.NewGuid():N}", Guid.NewGuid().ToString("N"),
                    0.7, "sig", DateTimeOffset.UtcNow);
                mutate(lead);
                db.Leads.Add(lead);
            }
        }
        AddLeads(pending, _ => { });                        // PendingReview (default)
        AddLeads(approved, l => l.Approve());
        AddLeads(sent, l => { l.Approve(); l.SetRecipient(EmailAddress.Create("a@b.com")); l.MarkSent(DateTimeOffset.UtcNow); });
        await db.SaveChangesAsync();
        return customer.Id.Value;
    }
}
```

Adjust `Customer.Create` / `Lead.SetRecipient` / `EmailAddress.Create` to the real signatures (check the domain; other tests like `LeadDetailTests.AddApprovedLeadWithRecipientAsync` show the exact calls for approve+recipient+marksent).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter FullyQualifiedName~NightlyHarvestTests`
Expected: FAIL to compile (`IdsDueForHarvestAsync` does not exist).

- [ ] **Step 3: Add the port method**

In `ICustomerRepository`:
```csharp
// System-level (no tenant context): ids of Full-plan customers that are not mid-harvest and have
// fewer than `threshold` unfinished (PendingReview/Approved) leads — i.e. have room for a top-up.
Task<IReadOnlyList<Guid>> IdsDueForHarvestAsync(int threshold, CancellationToken ct);
```

- [ ] **Step 4: Implement the query**

In `CustomerRepository` (mirror the `IgnoreQueryFilters()` style of the existing methods):
```csharp
public async Task<IReadOnlyList<Guid>> IdsDueForHarvestAsync(int threshold, CancellationToken ct)
{
    // Cross-customer system read → bypass the fail-closed tenant filter (like AllAsync/DeleteAsync).
    var due = await db.Customers.IgnoreQueryFilters()
        .Where(c => c.Plan == CustomerPlan.Full && c.HarvestStatus != HarvestStatus.Running)
        .Where(c => db.Leads.IgnoreQueryFilters()
            .Count(l => l.CustomerId == c.Id
                        && (l.Status == LeadStatus.PendingReview || l.Status == LeadStatus.Approved)) < threshold)
        .ToListAsync(ct);   // materialize, then map the value-object id in memory (avoids .Id.Value translation)
    return due.Select(c => c.Id.Value).ToList();
}
```
Note the correlated `db.Leads...Count(l => l.CustomerId == c.Id ...)` uses value-object *equality* (translates); the `.Id.Value` mapping happens in memory after `ToListAsync`. If materializing full `Customer` rows is undesirable at scale later, revisit with a projection — but at current scale this is correct and clear. Confirm `LeadStatus.PendingReview`/`Approved`, `CustomerPlan.Full`, `HarvestStatus.Running` are the exact enum member names.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter FullyQualifiedName~NightlyHarvestTests`
Expected: PASS (both facts). Watch for an EF translation exception at query time — if `.Count(l => l.CustomerId == c.Id ...)` fails to translate, that surfaces here; fix per the value-object gotcha before proceeding.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Application/Ports/ICustomerRepository.cs src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs tests/Coldstart.Web.Tests/NightlyHarvestTests.cs
git commit -m "feat(harvest): IdsDueForHarvestAsync — top-up policy query for the nightly harvest"
```

---

## Task 2: Nightly scheduler + options + wiring

**Files:**
- Create: `src/Coldstart.Infrastructure/Harvest/HarvestScheduleOptions.cs`
- Create: `src/Coldstart.Infrastructure/Harvest/NightlyHarvestScheduler.cs`
- Modify: `src/Coldstart.Infrastructure/DependencyInjection.cs`
- Modify: `src/Coldstart.Web/Program.cs`
- Modify: `src/Coldstart.Web/appsettings.json`
- Modify: `tests/Coldstart.Web.Tests/ColdstartWebFactory.cs`
- Modify: `tests/Coldstart.Web.Tests/NightlyHarvestTests.cs` (add scheduler tests)

**Interfaces:**
- Consumes: `ICustomerRepository.IdsDueForHarvestAsync` (Task 1), `IHarvestQueue.Enqueue(HarvestJob)`, `HarvestJob(Guid CustomerId)`.
- Produces: `HarvestScheduleOptions`; `NightlyHarvestScheduler` with `static TimeSpan NextRunDelay(TimeSpan runAt, DateTime utcNow)` and `static Task<int> EnqueueDueAsync(IServiceProvider sp, int threshold, CancellationToken ct)` (returns count enqueued).

- [ ] **Step 1: Write the failing scheduler tests**

Append to `NightlyHarvestTests.cs`:

```csharp
[Theory]
[InlineData(3, 0, 4, 0)]     // now 03:00, run 04:00 → 1h
[InlineData(4, 0, 4, 0)]     // now exactly 04:00 → fire now-ish (0), not +24h
[InlineData(5, 0, 4, 0)]     // now 05:00, run 04:00 → 23h (tomorrow)
public void NextRunDelay_ComputesDelayToNextRunAt(int nowH, int nowM, int runH, int runM)
{
    // Arrange
    var now = new DateTime(2026, 1, 1, nowH, nowM, 0, DateTimeKind.Utc);
    var runAt = new TimeSpan(runH, runM, 0);

    // Act
    var delay = Coldstart.Infrastructure.Harvest.NightlyHarvestScheduler.NextRunDelay(runAt, now);

    // Assert — always in [0, 24h), and lands exactly on the next runAt instant
    delay.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero).And.BeLessThan(TimeSpan.FromHours(24));
    (now + delay).TimeOfDay.Should().Be(runAt);
}

[Fact]
public async Task EnqueueDueAsync_EnqueuesExactlyTheDueCustomers()
{
    // Arrange
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
    var dueId = await SeedAsync(db, "sched-due", CustomerPlan.Full, HarvestStatus.Done, pending: 0);
    await SeedAsync(db, "sched-full", CustomerPlan.Full, HarvestStatus.Done, pending: 9);   // over threshold
    await SeedAsync(db, "sched-free", CustomerPlan.Free, HarvestStatus.Done, pending: 0);   // free

    var queue = factory.Services.GetRequiredService<Coldstart.Infrastructure.Harvest.HarvestQueue>();

    // Act
    var count = await Coldstart.Infrastructure.Harvest.NightlyHarvestScheduler.EnqueueDueAsync(scope.ServiceProvider, 5, default);

    // Assert — exactly the due customer was enqueued
    var enqueued = new List<Guid>();
    while (queue.Reader.TryRead(out var job)) enqueued.Add(job.CustomerId);
    enqueued.Should().Contain(dueId);
    enqueued.Should().OnlyContain(id => id == dueId);   // nothing else this run
    count.Should().Be(enqueued.Count);
}
```
Note: `HarvestQueue` is a singleton on the web factory, so reading its channel after `EnqueueDueAsync` observes what was enqueued. If other tests share the singleton and could interleave, assert with `Contain(dueId)` + that no *over-threshold/free* id appears, rather than strict equality — adjust to keep it robust. (The `HarvestQueueService` may also be draining; disabling nothing here is fine because reading a job out is what we assert. If drain races the read, prefer asserting the DB `HarvestStatus` transition instead — the implementer picks the robust assertion and documents the choice.)

- [ ] **Step 2: Run to verify it fails** — `dotnet test … --filter FullyQualifiedName~NightlyHarvestTests` → FAIL to compile (types don't exist).

- [ ] **Step 3: `HarvestScheduleOptions`**

```csharp
namespace Coldstart.Infrastructure.Harvest;

public sealed class HarvestScheduleOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan RunAtUtc { get; set; } = new(4, 0, 0);   // 04:00 UTC
    public int TopUpThreshold { get; set; } = 5;
}
```

- [ ] **Step 4: `NightlyHarvestScheduler`**

```csharp
using Coldstart.Application.Harvest;
using Coldstart.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Harvest;

// Fires once per day at RunAtUtc: enqueues a harvest for every Full-plan customer that has room
// (< TopUpThreshold unfinished leads). The existing HarvestQueueService drains them.
internal sealed class NightlyHarvestScheduler(
    IServiceScopeFactory scopes,
    IOptions<HarvestScheduleOptions> options,
    ILogger<NightlyHarvestScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled) { log.LogInformation("Nightly harvest scheduler disabled."); return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NextRunDelay(opts.RunAtUtc, DateTime.UtcNow);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                using var scope = scopes.CreateScope();
                var n = await EnqueueDueAsync(scope.ServiceProvider, opts.TopUpThreshold, stoppingToken);
                log.LogInformation("Nightly harvest: enqueued {Count} Full-plan customer(s).", n);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Nightly harvest scheduling failed.");
            }
        }
    }

    // Delay from utcNow to the next occurrence of runAt (today if still ahead, else tomorrow).
    public static TimeSpan NextRunDelay(TimeSpan runAt, DateTime utcNow)
    {
        var todayRun = utcNow.Date + runAt;
        var next = utcNow <= todayRun ? todayRun : todayRun.AddDays(1);
        return next - utcNow;
    }

    // Resolve the due customers and enqueue a job for each. Returns the count enqueued.
    public static async Task<int> EnqueueDueAsync(IServiceProvider sp, int threshold, CancellationToken ct)
    {
        var customers = sp.GetRequiredService<ICustomerRepository>();
        var queue = sp.GetRequiredService<IHarvestQueue>();
        var ids = await customers.IdsDueForHarvestAsync(threshold, ct);
        foreach (var id in ids) queue.Enqueue(new HarvestJob(id));
        return ids.Count;
    }
}
```
Confirm `HarvestJob`'s constructor shape (`new HarvestJob(Guid)`) against the real record.

- [ ] **Step 5: Register it**

In `DependencyInjection.AddHarvestQueue` (web-only, alongside the existing worker):
```csharp
services.AddHostedService<Harvest.NightlyHarvestScheduler>();
```
In `Program.cs` (composition root), bind the options from config:
```csharp
builder.Services.Configure<Coldstart.Infrastructure.Harvest.HarvestScheduleOptions>(
    builder.Configuration.GetSection("HarvestSchedule"));
```
In `appsettings.json`, add (documents the knobs; overridable via `HarvestSchedule__*` env in prod):
```json
"HarvestSchedule": { "Enabled": true, "RunAtUtc": "04:00:00", "TopUpThreshold": 5 }
```

- [ ] **Step 6: Keep tests hermetic**

In `ColdstartWebFactory.ConfigureWebHost`, disable the scheduler so the hosted service never fires during the suite (mirrors the existing `Quota:FreeLeadCount` override):
```csharp
builder.UseSetting("HarvestSchedule:Enabled", "false");
```
(The `EnqueueDueAsync`/`NextRunDelay` tests call the static methods directly, so they don't depend on the hosted service running.)

- [ ] **Step 7: Run to verify pass** — `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter FullyQualifiedName~NightlyHarvestTests` → PASS. Then the full `Coldstart.Web.Tests` project → green.

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat(harvest): nightly top-up scheduler (04:00 UTC, Full-plan, config-gated)"
```

---

## Self-review notes

- **Spec coverage:** top-up gate + threshold (Task 1 query), Full-only (query), skip-Running (query), nightly cadence (NextRunDelay + timer), config knobs + disable (options + factory), reuse of queue/pipeline (EnqueueDueAsync just enqueues). All mapped.
- **Type consistency:** `IdsDueForHarvestAsync(int, CancellationToken)` defined in Task 1, consumed in Task 2's `EnqueueDueAsync`. `HarvestJob(Guid CustomerId)` and `IHarvestQueue.Enqueue` are existing. `HarvestScheduleOptions` fields match the appsettings keys.
- **Known risks flagged:** EF value-object translation on the correlated count / id projection (Task 1 Step 4–5); the shared-singleton queue assertion robustness (Task 2 Step 1 note).

# Onboarding Harvest & Free-Tier Foil Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When an admin invites a customer, capture their niche/info, generate a per-customer search profile, send the invite immediately, and run a background harvest so the new user sees niche-relevant leads on first login — the first 3 unlocked, the rest blinded behind an upgrade CTA.

**Architecture:** Hexagonal .NET 10 (Domain ← Application ← Infrastructure + Web). Invite generates a search profile via an LLM port, provisions a tenant with that profile, sends the invite, and enqueues a harvest onto an in-process channel drained by a `BackgroundService` (one job at a time). Quota/foil is enforced server-side in the read models and workflow, gated by a new `Customer.Plan`.

**Tech Stack:** .NET 10 Razor Pages, EF Core + Npgsql, ASP.NET Core Identity, Groq LLM (llama-3.3-70b), `System.Threading.Channels`, `BackgroundService`. Tests: xUnit 2.9 + NSubstitute 5.3 + FluentAssertions 7 + Testcontainers.PostgreSql 4.13.

## Global Constraints

- **CS-HEX-01** Domain has zero project dependencies (no EF/ASP.NET). **CS-HEX-02** Ports live in Application. **CS-HEX-03** DI only in composition roots.
- **CS-DDD-01/02** Value objects are `record`; entities use `private set`/`init` and expose behaviour, not raw setters. **CS-DATA-01** No `IQueryable` crosses Infrastructure→Application. **CS-DATA-03** async all the way, `CancellationToken` down to IO. **CS-MOD-01** `internal` for impls; `public` only for ports/DTOs/domain.
- Free-lead quota is **fixed global `3`** via config (`Quota:FreeLeadCount`, default 3). Not per-customer editable.
- `Customer.Plan` defaults to **`Free`**; the operator's own "Coldstart" tenant is seeded **`Full`**.
- Unlocked set = the first `FreeLeadCount` of a customer's leads ordered by **`Fit` asc (Yes<Maybe<No`), `Score` desc, `CreatedAt` asc, `Id` asc**, over ALL the customer's leads (any status). `Full` plan → nothing locked.
- Locked leads are refused server-side: `GetLeadDetail` returns null; `ILeadWorkflow` actions return `NotFound`.
- Harvest at invite is **background, one job at a time**. Invite + email never block on it.
- Tests: strict AAA with `// Arrange`/`// Act`/`// Assert`; only mock outbound ports; build domain objects directly; per-test unique emails/fingerprints (`Guid.NewGuid():N`) because the class DB is shared. FluentAssertions v7 API.
- Migrations: `dotnet ef migrations add <Name> --project src/Coldstart.Infrastructure --startup-project src/Coldstart.Web --output-dir Migrations`. The Design package + `DesignTimeDbContextFactory` already exist in Infrastructure.
- Never add a `Co-Authored-By`/"Generated with Claude" trailer to commits.

---

## File Structure

**New (Domain):** `CustomerPlan.cs`, `HarvestStatus.cs` (enums).
**New (Application):** `Leads/QuotaOptions.cs`, `Leads/LeadLocking.cs`, `Customers/ISearchProfileGenerator.cs` (+ `SearchProfileRequest`/`SearchProfile`), `Customers/SearchProfileParser.cs`, `Customers/CompanyProfileSeed.cs`, `Harvest/HarvestTargeting.cs`, `Harvest/IHarvestQueue.cs` (+ `HarvestJob`).
**New (Infrastructure):** `Harvest/GroqSearchProfileGenerator.cs`, `Harvest/HarvestQueue.cs`, `Harvest/HarvestQueueService.cs`, migration `AddPlanHarvestAndSearchProfile`.
**New (Web tests):** `FakeHarvestQueue.cs`, `FakeSearchProfileGenerator.cs`, `FoilWebFactory.cs`, `OnboardingInviteTests.cs`, `LeadFoilTests.cs`.

**Modified (Domain):** `Customer.cs` (Plan/HarvestStatus/LastHarvestAt + behaviour), `CompanyProfile.cs` (Offer/TargetKeywords/BuyingSignals/Disqualifiers).
**Modified (Application):** `Leads/LeadSummary.cs` (+`Locked`), `Leads/LeadInbox.cs` (+`Plan`/`LockedCount`/`HarvestStatus`), `Leads/ListLeads.cs` (locking), `Leads/GetLeadInbox.cs`, `Leads/GetLeadDetail.cs`, `Leads/LeadWorkflow.cs`, `Customers/IProvisionCustomer.cs` + `ProvisionCustomer.cs`, `Harvest/IHarvestLeads.cs` + `HarvestLeads.cs`, `Ports/IInquiryRepository.cs`, `DependencyInjection.cs`.
**Modified (Infrastructure):** `Configurations/CustomerConfiguration.cs`, `Repositories/InquiryRepository.cs`, `Tenancy/HttpTenantContext.cs` (fallback), `DependencyInjection.cs`, `Coldstart.Infrastructure.csproj`.
**Modified (Web):** `Program.cs`, `Identity/IdentitySeeder.cs`, `Areas/Admin/Pages/Index.cshtml(.cs)`, `Areas/App/Pages/Index.cshtml(.cs)`, `appsettings*.json`.
**Modified (Web tests):** `ColdstartWebFactory.cs` (register fakes + high quota), `IdentitySeedTests.cs`, `GetLeadInboxTests.cs`, `ListLeadsTests.cs`.

---

## Task 1: Customer.Plan / HarvestStatus + CompanyProfile search fields + EF config + migration

Domain model + EF mapping + one migration together, so the whole suite stays green (a model change without its migration breaks Web startup).

**Files:**
- Create: `src/Coldstart.Domain/CustomerPlan.cs`, `src/Coldstart.Domain/HarvestStatus.cs`
- Modify: `src/Coldstart.Domain/Customer.cs`, `src/Coldstart.Domain/CompanyProfile.cs`, `src/Coldstart.Infrastructure/Configurations/CustomerConfiguration.cs`
- Test: `tests/Coldstart.Domain.Tests/CustomerTests.cs`, `tests/Coldstart.Infrastructure.Tests/CustomerRepositoryTests.cs`

**Interfaces produced:**
- `enum CustomerPlan { Free, Full }`, `enum HarvestStatus { None, Pending, Running, Done, Failed }`
- `Customer.Plan`, `Customer.HarvestStatus`, `Customer.LastHarvestAt`; `Customer.SetPlan(CustomerPlan)`, `Customer.MarkHarvest(HarvestStatus, DateTimeOffset?)`
- `CompanyProfile` gains `string? Offer, string? TargetKeywords, string? BuyingSignals, string? Disqualifiers` (positional, after `MentionSourceTool`, all defaulted null)

- [ ] **Step 1: Write failing Domain tests** — append to `CustomerTests.cs`:

```csharp
    [Fact]
    public void Create_DefaultsToFreePlanAndNoHarvest()
    {
        // Act
        var customer = Customer.Create("Acme");

        // Assert
        customer.Plan.Should().Be(CustomerPlan.Free);
        customer.HarvestStatus.Should().Be(HarvestStatus.None);
        customer.LastHarvestAt.Should().BeNull();
    }

    [Fact]
    public void SetPlan_ChangesPlan()
    {
        // Arrange
        var customer = Customer.Create("Acme");

        // Act
        customer.SetPlan(CustomerPlan.Full);

        // Assert
        customer.Plan.Should().Be(CustomerPlan.Full);
    }

    [Fact]
    public void MarkHarvest_SetsStatusAndStampsTimeWhenGiven()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        var at = DateTimeOffset.UtcNow;

        // Act
        customer.MarkHarvest(HarvestStatus.Running, null);   // no timestamp yet
        customer.MarkHarvest(HarvestStatus.Done, at);

        // Assert
        customer.HarvestStatus.Should().Be(HarvestStatus.Done);
        customer.LastHarvestAt.Should().Be(at);
    }
```

- [ ] **Step 2: Run — expect FAIL** (compile errors: `CustomerPlan`/`HarvestStatus`/members missing).

Run: `dotnet test tests/Coldstart.Domain.Tests`

- [ ] **Step 3: Add the enums**

`src/Coldstart.Domain/CustomerPlan.cs`:
```csharp
namespace Coldstart.Domain;

public enum CustomerPlan { Free, Full }
```

`src/Coldstart.Domain/HarvestStatus.cs`:
```csharp
namespace Coldstart.Domain;

public enum HarvestStatus { None, Pending, Running, Done, Failed }
```

- [ ] **Step 4: Extend `Customer`** — add fields + behaviour (keep everything else). Insert after the `Profile` property and add methods near `UpdateProfile`:

```csharp
    public CompanyProfile Profile { get; private set; } = CompanyProfile.Empty;
    public CustomerPlan Plan { get; private set; } = CustomerPlan.Free;
    public HarvestStatus HarvestStatus { get; private set; } = HarvestStatus.None;
    public DateTimeOffset? LastHarvestAt { get; private set; }
```
```csharp
    public void SetPlan(CustomerPlan plan) => Plan = plan;

    public void MarkHarvest(HarvestStatus status, DateTimeOffset? at)
    {
        HarvestStatus = status;
        if (at is not null) LastHarvestAt = at;
    }
```

- [ ] **Step 5: Extend `CompanyProfile`** — add the four search fields (positional, defaulted). Replace the record header:

```csharp
public sealed record CompanyProfile(
    string? CompanyName,
    string? Address,
    string? VatNumber,
    string? KvkNumber,
    string? Niche,
    string? Website,
    string? ContactEmail,
    string? Brief = null,
    bool MentionSourceTool = false,
    string? Offer = null,
    string? TargetKeywords = null,
    string? BuyingSignals = null,
    string? Disqualifiers = null)
{
    public static CompanyProfile Empty { get; } = new(null, null, null, null, null, null, null);
}
```

- [ ] **Step 6: Run Domain tests — expect PASS.** Run: `dotnet test tests/Coldstart.Domain.Tests`

- [ ] **Step 7: EF config** — in `CustomerConfiguration.Configure`, after the `Property(c => c.Name)` line add the Customer columns, and inside the `OwnsOne(c => c.Profile, p => {...})` block add the four profile columns:

```csharp
        builder.Property(c => c.Name).IsRequired();
        builder.Property(c => c.Plan).HasConversion<string>().HasMaxLength(16).HasDefaultValue(CustomerPlan.Free);
        builder.Property(c => c.HarvestStatus).HasConversion<string>().HasMaxLength(16).HasDefaultValue(HarvestStatus.None);
        builder.Property(c => c.LastHarvestAt);
```
```csharp
            p.Property(x => x.MentionSourceTool).HasColumnName("ProfileMentionSourceTool").HasDefaultValue(false);
            p.Property(x => x.Offer).HasColumnName("ProfileOffer");
            p.Property(x => x.TargetKeywords).HasColumnName("ProfileTargetKeywords");
            p.Property(x => x.BuyingSignals).HasColumnName("ProfileBuyingSignals");
            p.Property(x => x.Disqualifiers).HasColumnName("ProfileDisqualifiers");
```
Add `using Coldstart.Domain;` if not present (it is).

- [ ] **Step 8: Write failing Infra persistence test** — append to `CustomerRepositoryTests.cs` (uses the existing `PostgresFixture` pattern; class already has `IClassFixture<PostgresFixture>`):

```csharp
    [Fact]
    public async Task Update_PersistsPlanHarvestStatusAndSearchProfile()
    {
        // Arrange — provision a customer, then set plan + harvest + a generated search profile
        var name = $"Cust-{Guid.NewGuid():N}";
        Guid id;
        await using (var seed = fixture.NewDbContext())
        {
            var c = Customer.Create(name);
            seed.Customers.Add(c);
            await seed.SaveChangesAsync();
            id = c.Id.Value;
        }
        await using (var write = fixture.NewDbContext(/* tenant */ null))
        {
            var repo = new CustomerRepository(write);
            var c = await repo.GetByNameAsync(name, CancellationToken.None);
            c!.SetPlan(CustomerPlan.Full);
            c.MarkHarvest(HarvestStatus.Done, DateTimeOffset.UtcNow);
            c.UpdateProfile(new CompanyProfile("Co", null, null, null, "dentists", null, null,
                Brief: "we sell scheduling", MentionSourceTool: false,
                Offer: "scheduling software", TargetKeywords: "dentist\nclinic",
                BuyingSignals: "hiring\nexpanding", Disqualifiers: "enterprise"));
            await repo.UpdateAsync(c, CancellationToken.None);
        }

        // Act — read it back (GetByName bypasses the tenant filter)
        await using var read = fixture.NewDbContext();
        var loaded = await new CustomerRepository(read).GetByNameAsync(name, CancellationToken.None);

        // Assert
        loaded!.Plan.Should().Be(CustomerPlan.Full);
        loaded.HarvestStatus.Should().Be(HarvestStatus.Done);
        loaded.Profile.TargetKeywords.Should().Be("dentist\nclinic");
        loaded.Profile.Offer.Should().Be("scheduling software");
    }
```
Add `using Coldstart.Domain;` to the test file if missing.

- [ ] **Step 9: Generate the migration**

Run:
```bash
dotnet ef migrations add AddPlanHarvestAndSearchProfile \
  --project src/Coldstart.Infrastructure --startup-project src/Coldstart.Web --output-dir Migrations
```
Expected: a new `Migrations/*_AddPlanHarvestAndSearchProfile.cs` adding `Plan` (text, default `Free`), `HarvestStatus` (text, default `None`), `LastHarvestAt` (timestamptz null) to `customers`, and `ProfileOffer`/`ProfileTargetKeywords`/`ProfileBuyingSignals`/`ProfileDisqualifiers` (text null). Open it and confirm the `Plan`/`HarvestStatus` `defaultValue:` are `"Free"`/`"None"`.

- [ ] **Step 10: Run Infra tests — expect PASS** (Testcontainers builds the schema from the model via `EnsureCreated`; Web tests apply the migration). Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter CustomerRepositoryTests`

- [ ] **Step 11: Commit**
```bash
git add -A && git commit -m "feat(domain): Customer.Plan/HarvestStatus + CompanyProfile search fields + migration"
```

---

## Task 2: QuotaOptions + LeadLocking + Locked read-model fields

Pure ranking core reused everywhere, plus the read-model fields the inbox/detail/workflow need.

**Files:**
- Create: `src/Coldstart.Application/Leads/QuotaOptions.cs`, `src/Coldstart.Application/Leads/LeadLocking.cs`
- Modify: `src/Coldstart.Application/Leads/LeadSummary.cs`, `src/Coldstart.Application/Leads/LeadInbox.cs`, `src/Coldstart.Application/Leads/ListLeads.cs`, `src/Coldstart.Application/DependencyInjection.cs`
- Test: `tests/Coldstart.Application.Tests/LeadLockingTests.cs` (new), `tests/Coldstart.Application.Tests/ListLeadsTests.cs`

**Interfaces produced:**
- `sealed class QuotaOptions { int FreeLeadCount = 3 }`
- `static class LeadLocking { HashSet<Guid> LockedLeadIds(IReadOnlyList<Lead> leads, CustomerPlan plan, int freeCount) }`
- `LeadSummary` gains `bool Locked = false` (trailing, defaulted)
- `LeadInbox(string CustomerName, IReadOnlyList<LeadSummary> Leads, CustomerPlan Plan, int LockedCount, HarvestStatus HarvestStatus)`
- `IListLeads.ForCustomerAsync(CustomerId, CustomerPlan, int freeCount, CancellationToken)` (signature change)

- [ ] **Step 1: Write failing `LeadLockingTests.cs`**

```csharp
using Coldstart.Application.Leads;
using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class LeadLockingTests
{
    private static Lead Harvested(Fit fit, double score, DateTimeOffset created) =>
        Lead.CreateHarvested(new CustomerId(Guid.NewGuid()), "Co", fit, "s", "b",
            "hn", "https://x/1", Guid.NewGuid().ToString("N"), score, "sig", created);

    [Fact]
    public void LockedLeadIds_FreePlan_LocksEverythingBeyondTheFreeCount()
    {
        // Arrange — 5 leads, quota 3
        var now = DateTimeOffset.UtcNow;
        var leads = new[]
        {
            Harvested(Fit.Yes, 0.9, now), Harvested(Fit.Yes, 0.8, now),
            Harvested(Fit.Maybe, 0.7, now), Harvested(Fit.Maybe, 0.6, now),
            Harvested(Fit.No, 0.5, now),
        };

        // Act
        var locked = LeadLocking.LockedLeadIds(leads, CustomerPlan.Free, 3);

        // Assert — top 3 unlocked, last 2 locked
        locked.Should().HaveCount(2);
        locked.Should().Contain(leads[3].Id.Value).And.Contain(leads[4].Id.Value);
        locked.Should().NotContain(leads[0].Id.Value);
    }

    [Fact]
    public void LockedLeadIds_RanksByFitThenScore_NotInsertionOrder()
    {
        // Arrange — a low-fit high-score lead must NOT outrank a high-fit lead
        var now = DateTimeOffset.UtcNow;
        var no = Harvested(Fit.No, 0.99, now);
        var yes = Harvested(Fit.Yes, 0.10, now);

        // Act — quota 1
        var locked = LeadLocking.LockedLeadIds(new[] { no, yes }, CustomerPlan.Free, 1);

        // Assert — the Yes is unlocked, the No is locked
        locked.Should().Contain(no.Id.Value);
        locked.Should().NotContain(yes.Id.Value);
    }

    [Fact]
    public void LockedLeadIds_FullPlan_LocksNothing()
    {
        // Arrange
        var leads = new[] { Harvested(Fit.Yes, 0.9, DateTimeOffset.UtcNow), Harvested(Fit.No, 0.1, DateTimeOffset.UtcNow) };

        // Act
        var locked = LeadLocking.LockedLeadIds(leads, CustomerPlan.Full, 1);

        // Assert
        locked.Should().BeEmpty();
    }

    [Fact]
    public void LockedLeadIds_FewerLeadsThanQuota_LocksNothing()
    {
        // Arrange
        var leads = new[] { Harvested(Fit.Yes, 0.9, DateTimeOffset.UtcNow) };

        // Act
        var locked = LeadLocking.LockedLeadIds(leads, CustomerPlan.Free, 3);

        // Assert
        locked.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (`QuotaOptions`/`LeadLocking` missing). Run: `dotnet test tests/Coldstart.Application.Tests --filter LeadLockingTests`

- [ ] **Step 3: Add `QuotaOptions.cs`**
```csharp
namespace Coldstart.Application.Leads;

public sealed class QuotaOptions
{
    public int FreeLeadCount { get; set; } = 3;
}
```

- [ ] **Step 4: Add `LeadLocking.cs`**
```csharp
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

// Which of a customer's leads are locked behind the free-tier foil. Stable, ungameable ranking:
// Fit asc (Yes<Maybe<No), Score desc, CreatedAt asc, Id asc — computed over ALL leads (any status),
// so acting on a free lead never promotes a locked one. Full plan locks nothing.
public static class LeadLocking
{
    public static HashSet<Guid> LockedLeadIds(IReadOnlyList<Lead> leads, CustomerPlan plan, int freeCount)
    {
        if (plan == CustomerPlan.Full || freeCount < 0) return [];
        return leads
            .OrderBy(l => l.Fit)
            .ThenByDescending(l => l.Score)
            .ThenBy(l => l.CreatedAt)
            .ThenBy(l => l.Id.Value)
            .Skip(freeCount)
            .Select(l => l.Id.Value)
            .ToHashSet();
    }
}
```

- [ ] **Step 5: Run `LeadLockingTests` — expect PASS.**

- [ ] **Step 6: Extend `LeadSummary`** — add trailing `Locked`:
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
    double Score,
    DateTimeOffset CreatedAt,
    string MatchedSignals,
    bool Locked = false);
```

- [ ] **Step 7: Extend `LeadInbox`**
```csharp
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

public sealed record LeadInbox(
    string CustomerName,
    IReadOnlyList<LeadSummary> Leads,
    CustomerPlan Plan,
    int LockedCount,
    HarvestStatus HarvestStatus);
```

- [ ] **Step 8: Update `ListLeads`** — take plan + freeCount, compute locking, tag summaries:
```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

internal sealed class ListLeads(ILeadRepository leads) : IListLeads
{
    public async Task<IReadOnlyList<LeadSummary>> ForCustomerAsync(CustomerId customerId, CustomerPlan plan, int freeCount, CancellationToken ct)
    {
        var found = await leads.ForCustomerAsync(customerId, ct);
        var locked = LeadLocking.LockedLeadIds(found, plan, freeCount);
        return found
            .Select(l => new LeadSummary(
                l.Id.Value, l.Company, l.Fit.ToString(), l.Status.ToString(),
                l.RecipientEmail?.Value, l.EmailConfidence.ToString(), l.Subject, l.Draft,
                l.Source, l.Score, l.CreatedAt, l.MatchedSignals,
                Locked: locked.Contains(l.Id.Value)))
            .ToList();
    }
}
```
And update `IListLeads`:
```csharp
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

public interface IListLeads
{
    Task<IReadOnlyList<LeadSummary>> ForCustomerAsync(CustomerId customerId, CustomerPlan plan, int freeCount, CancellationToken ct);
}
```

- [ ] **Step 9: Register default `QuotaOptions`** — in `Application/DependencyInjection.cs` add `using Microsoft.Extensions.DependencyInjection.Extensions;` and inside `AddApplication`:
```csharp
        services.TryAddSingleton(new Leads.QuotaOptions());
```
(The Web composition root overrides this with a config-bound instance in Task 9.)

- [ ] **Step 10: Fix `ListLeadsTests`** — update every `ForCustomerAsync(id, ct)` call to `ForCustomerAsync(id, CustomerPlan.Free, 3, ct)` (or `CustomerPlan.Full` where the test asserts all leads present regardless of count), add `using Coldstart.Domain;`, and add one locking test:
```csharp
    [Fact]
    public async Task ForCustomerAsync_FreePlan_TagsLeadsBeyondQuotaAsLocked()
    {
        // Arrange — 2 leads, quota 1
        var cid = new CustomerId(Guid.NewGuid());
        var l1 = Lead.CreateHarvested(cid, "A", Fit.Yes, "s", "b", "hn", "https://x/1", "fp1", 0.9, "x", DateTimeOffset.UtcNow);
        var l2 = Lead.CreateHarvested(cid, "B", Fit.Yes, "s", "b", "hn", "https://x/2", "fp2", 0.8, "x", DateTimeOffset.UtcNow);
        var repo = Substitute.For<ILeadRepository>();
        repo.ForCustomerAsync(cid, Arg.Any<CancellationToken>()).Returns(new[] { l1, l2 });
        var sut = new ListLeads(repo);

        // Act
        var result = await sut.ForCustomerAsync(cid, CustomerPlan.Free, 1, CancellationToken.None);

        // Assert
        result.Single(s => s.Id == l1.Id.Value).Locked.Should().BeFalse();
        result.Single(s => s.Id == l2.Id.Value).Locked.Should().BeTrue();
    }
```

- [ ] **Step 11: Update `GetLeadInbox` to its final form + rewrite its tests** (the read-model records + `IListLeads` signature just changed, so `GetLeadInbox` must change in this same task or the Application project won't compile).

Rewrite `src/Coldstart.Application/Leads/GetLeadInbox.cs`:
```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

internal sealed class GetLeadInbox(ICustomerRepository customers, IListLeads listLeads, QuotaOptions quota) : IGetLeadInbox
{
    public async Task<LeadInbox> ForCustomerAsync(Guid customerId, CancellationToken ct)
    {
        var id = new CustomerId(customerId);
        var customer = await customers.GetAsync(id, ct);
        if (customer is null)
            return new LeadInbox("—", Array.Empty<LeadSummary>(), CustomerPlan.Free, 0, HarvestStatus.None);

        var leads = await listLeads.ForCustomerAsync(id, customer.Plan, quota.FreeLeadCount, ct);
        var lockedCount = leads.Count(l => l.Locked);
        return new LeadInbox(customer.Name, leads, customer.Plan, lockedCount, customer.HarvestStatus);
    }
}
```
Rewrite `tests/Coldstart.Application.Tests/GetLeadInboxTests.cs` (the old ones use the 2-arg `IListLeads`/`LeadInbox`):
```csharp
using Coldstart.Application.Leads;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class GetLeadInboxTests
{
    [Fact]
    public async Task ForCustomerAsync_WhenCustomerExists_ReturnsInboxWithPlanAndLockedCount()
    {
        // Arrange — a Free customer, one locked + one unlocked summary from ListLeads
        var customer = Customer.Create("Coldstart");
        customer.MarkHarvest(HarvestStatus.Done, DateTimeOffset.UtcNow);
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var open = new LeadSummary(Guid.NewGuid(), "A", "Yes", "PendingReview", null, "None", "s", "b", "hn", 0.9, DateTimeOffset.UtcNow, "x", Locked: false);
        var shut = new LeadSummary(Guid.NewGuid(), "B", "Yes", "PendingReview", null, "None", "s", "b", "hn", 0.8, DateTimeOffset.UtcNow, "x", Locked: true);
        var listLeads = Substitute.For<IListLeads>();
        listLeads.ForCustomerAsync(customer.Id, CustomerPlan.Free, 3, Arg.Any<CancellationToken>()).Returns(new[] { open, shut });
        var sut = new GetLeadInbox(customers, listLeads, new QuotaOptions { FreeLeadCount = 3 });

        // Act
        var result = await sut.ForCustomerAsync(customer.Id.Value, CancellationToken.None);

        // Assert
        result.CustomerName.Should().Be("Coldstart");
        result.Plan.Should().Be(CustomerPlan.Free);
        result.HarvestStatus.Should().Be(HarvestStatus.Done);
        result.LockedCount.Should().Be(1);
        result.Leads.Should().HaveCount(2);
    }

    [Fact]
    public async Task ForCustomerAsync_WhenCustomerUnknown_ReturnsEmptyInboxWithoutCallingListLeads()
    {
        // Arrange
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        var listLeads = Substitute.For<IListLeads>();
        var sut = new GetLeadInbox(customers, listLeads, new QuotaOptions());

        // Act
        var result = await sut.ForCustomerAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.CustomerName.Should().Be("—");
        result.Leads.Should().BeEmpty();
        result.Plan.Should().Be(CustomerPlan.Free);
        result.HarvestStatus.Should().Be(HarvestStatus.None);
        await listLeads.DidNotReceive().ForCustomerAsync(Arg.Any<CustomerId>(), Arg.Any<CustomerPlan>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 12: Run the Application read-path tests — expect PASS.** Run: `dotnet test tests/Coldstart.Application.Tests --filter "LeadLockingTests|ListLeadsTests|GetLeadInboxTests"`

- [ ] **Step 13: Commit**
```bash
git add -A && git commit -m "feat(leads): quota + lead-locking + inbox carries plan/locked-count/harvest-status"
```

---

## Task 3: Refuse locked leads in GetLeadDetail + LeadWorkflow

Server-side enforcement so a crafted URL or drag can't reach a locked lead. Free customers only.

**Files:**
- Modify: `src/Coldstart.Application/Leads/GetLeadDetail.cs`, `src/Coldstart.Application/Leads/LeadWorkflow.cs`
- Test: `tests/Coldstart.Application.Tests/GetLeadDetailTests.cs`, `tests/Coldstart.Application.Tests/LeadWorkflowTests.cs`

**Interfaces consumed:** `LeadLocking.LockedLeadIds`, `QuotaOptions`, `ICustomerRepository.GetAsync`, `ILeadRepository.ForCustomerAsync`.

**Design note:** both use-cases add a shared private guard: load the customer (plan) + all its leads, compute the locked set, check membership. `GetLeadDetail` returns `null` for a locked lead; `LeadWorkflow` returns `LeadActionResult.NotFound`. The guard runs AFTER the existing ownership check so cross-tenant behaviour is unchanged.

- [ ] **Step 1: Write failing `GetLeadDetailTests` case** — append (class is a plain `public class GetLeadDetailTests`):

```csharp
    [Fact]
    public async Task ForLeadAsync_WhenLeadIsLockedForFreeCustomer_ReturnsNull()
    {
        // Arrange — Free customer, quota 1, two leads; the 2nd (lower score) is locked
        var cid = Guid.NewGuid();
        var customer = Customer.Create("Acme");   // Free
        var l1 = Lead.CreateHarvested(new CustomerId(cid), "A", Fit.Yes, "s", "b", "hn", "https://x/1", "fp1", 0.9, "x", DateTimeOffset.UtcNow);
        var l2 = Lead.CreateHarvested(new CustomerId(cid), "B", Fit.Yes, "s", "b", "hn", "https://x/2", "fp2", 0.5, "x", DateTimeOffset.UtcNow);
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(customer);
        var leads = Substitute.For<ILeadRepository>();
        leads.GetAsync(l2.Id, Arg.Any<CancellationToken>()).Returns(l2);
        leads.ForCustomerAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(new[] { l1, l2 });
        var sut = new GetLeadDetail(leads, customers, new QuotaOptions { FreeLeadCount = 1 });

        // Act
        var detail = await sut.ForLeadAsync(cid, l2.Id.Value, CancellationToken.None);

        // Assert
        detail.Should().BeNull();
    }

    [Fact]
    public async Task ForLeadAsync_WhenLeadIsUnlocked_ReturnsDetail()
    {
        // Arrange — the top lead under quota 1 is unlocked
        var cid = Guid.NewGuid();
        var customer = Customer.Create("Acme");
        var l1 = Lead.CreateHarvested(new CustomerId(cid), "A", Fit.Yes, "s", "b", "hn", "https://x/1", "fp1", 0.9, "x", DateTimeOffset.UtcNow);
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(customer);
        var leads = Substitute.For<ILeadRepository>();
        leads.GetAsync(l1.Id, Arg.Any<CancellationToken>()).Returns(l1);
        leads.ForCustomerAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(new[] { l1 });
        var sut = new GetLeadDetail(leads, customers, new QuotaOptions { FreeLeadCount = 1 });

        // Act
        var detail = await sut.ForLeadAsync(cid, l1.Id.Value, CancellationToken.None);

        // Assert
        detail.Should().NotBeNull();
    }
```
Ensure the file has `using Coldstart.Application.Leads; using Coldstart.Application.Ports; using Coldstart.Domain; using NSubstitute;` (match existing header; add any missing).

- [ ] **Step 2: Run — expect FAIL** (ctor arity). Run: `dotnet test tests/Coldstart.Application.Tests --filter GetLeadDetailTests`

- [ ] **Step 3: Update `GetLeadDetail`**
```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

internal sealed class GetLeadDetail(ILeadRepository leads, ICustomerRepository customers, QuotaOptions quota) : IGetLeadDetail
{
    public async Task<LeadDetail?> ForLeadAsync(Guid customerId, Guid leadId, CancellationToken ct)
    {
        var lead = await leads.GetAsync(new LeadId(leadId), ct);
        if (lead is null || lead.CustomerId.Value != customerId) return null;

        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is not null)
        {
            var all = await leads.ForCustomerAsync(new CustomerId(customerId), ct);
            if (LeadLocking.LockedLeadIds(all, customer.Plan, quota.FreeLeadCount).Contains(leadId))
                return null;   // locked behind the free-tier foil — not accessible
        }

        return new LeadDetail(
            lead.Id.Value, lead.Company, lead.Fit.ToString(), lead.Status.ToString(),
            lead.Subject, lead.Draft, lead.RecipientEmail?.Value,
            lead.Source, lead.SourceUrl, lead.Score, lead.SentAt,
            ContactLinkBuilder.Build(lead.Company, lead.SourceUrl),
            lead.MatchedSignals,
            lead.EmailConfidence.ToString(),
            lead.Notes.OrderByDescending(n => n.CreatedAt).Select(n => new LeadNoteView(n.Id, n.Text, n.CreatedAt, n.UpdatedAt)).ToList());
    }
}
```

- [ ] **Step 4: Run — expect PASS** (GetLeadDetailTests).

- [ ] **Step 5: Write failing `LeadWorkflowTests` case** — append. Existing tests construct `new LeadWorkflow(leads, outreach)`; the ctor gains two args, so **update every existing `new LeadWorkflow(...)` in this file** to `new LeadWorkflow(leads, outreach, customers, new QuotaOptions())` where `customers` is a `Substitute.For<ICustomerRepository>()` that returns a `Customer.Create("X")` (Free) — for existing tests that customer has ≤ quota leads, so nothing locks and their behaviour is unchanged. Add a helper at the top of the class and the new test:

```csharp
    private static (ICustomerRepository customers, Customer customer) FreeCustomer(Guid id)
    {
        var customer = Customer.Create("Acme");   // Free by default
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(new CustomerId(id), Arg.Any<CancellationToken>()).Returns(customer);
        return (customers, customer);
    }

    [Fact]
    public async Task ApproveAsync_WhenLeadIsLocked_ReturnsNotFound()
    {
        // Arrange — Free customer, quota 1; act on the locked (2nd) lead
        var cid = Guid.NewGuid();
        var (customers, _) = FreeCustomer(cid);
        var l1 = Lead.CreateHarvested(new CustomerId(cid), "A", Fit.Yes, "s", "b", "hn", "https://x/1", "fp1", 0.9, "x", DateTimeOffset.UtcNow);
        var l2 = Lead.CreateHarvested(new CustomerId(cid), "B", Fit.Yes, "s", "b", "hn", "https://x/2", "fp2", 0.5, "x", DateTimeOffset.UtcNow);
        var leads = Substitute.For<ILeadRepository>();
        leads.GetAsync(l2.Id, Arg.Any<CancellationToken>()).Returns(l2);
        leads.ForCustomerAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(new[] { l1, l2 });
        var outreach = Substitute.For<Coldstart.Application.Outreach.IOutreachMailer>();
        var sut = new LeadWorkflow(leads, outreach, customers, new QuotaOptions { FreeLeadCount = 1 });

        // Act
        var result = await sut.ApproveAsync(cid, l2.Id.Value, CancellationToken.None);

        // Assert — refused, and never persisted
        result.Status.Should().Be(LeadActionStatus.NotFound);
        await leads.DidNotReceive().UpdateAsync(Arg.Any<Lead>(), Arg.Any<CancellationToken>());
    }
```
Add `using Coldstart.Domain;` if missing.

- [ ] **Step 6: Run — expect FAIL** (ctor arity + no lock guard). Run: `dotnet test tests/Coldstart.Application.Tests --filter LeadWorkflowTests`

- [ ] **Step 7: Update `LeadWorkflow`** — add ctor deps + a guard used by both choke points:

Change the class header:
```csharp
internal sealed class LeadWorkflow(ILeadRepository leads, IOutreachMailer outreach, ICustomerRepository customers, QuotaOptions quota) : ILeadWorkflow
```
Add `using Coldstart.Domain;` (for `CustomerId`/`LeadLocking`) and a guard method:
```csharp
    // Locked leads (beyond the free-tier quota) are not actionable. Runs after the ownership check.
    private async Task<bool> IsLockedAsync(Guid customerId, Guid leadId, CancellationToken ct)
    {
        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is null) return false;
        var all = await leads.ForCustomerAsync(new CustomerId(customerId), ct);
        return Leads.LeadLocking.LockedLeadIds(all, customer.Plan, quota.FreeLeadCount).Contains(leadId);
    }
```
In `MutateAsync`, add the guard right after the ownership check:
```csharp
        var lead = await leads.GetAsync(new LeadId(leadId), ct);
        if (lead is null || lead.CustomerId.Value != customerId)
            return LeadActionResult.NotFound;
        if (await IsLockedAsync(customerId, leadId, ct))
            return LeadActionResult.NotFound;
```
In `SendFromColdstartAsync`, add it right after its ownership check:
```csharp
        var lead = await leads.GetAsync(new LeadId(leadId), ct);
        if (lead is null || lead.CustomerId.Value != customerId)
            return LeadActionResult.NotFound;
        if (await IsLockedAsync(customerId, leadId, ct))
            return LeadActionResult.NotFound;
```
(`LeadLocking` is in the same `Coldstart.Application.Leads` namespace, so `Leads.LeadLocking` also resolves as `LeadLocking` — either is fine.)

- [ ] **Step 8: Run — expect PASS** (whole Application suite green now). Run: `dotnet test tests/Coldstart.Application.Tests`

- [ ] **Step 9: Commit**
```bash
git add -A && git commit -m "feat(leads): refuse locked leads in detail + workflow (server-side foil)"
```

---

## Task 4: Search-profile generation (port + parser + Groq impl)

Turns a prospect's niche + info into a lead-search profile (offer, keywords, buying signals, disqualifiers).

**Files:**
- Create: `src/Coldstart.Application/Customers/ISearchProfileGenerator.cs`, `src/Coldstart.Application/Customers/SearchProfileParser.cs`, `src/Coldstart.Infrastructure/Harvest/GroqSearchProfileGenerator.cs`
- Modify: `src/Coldstart.Infrastructure/DependencyInjection.cs`
- Test: `tests/Coldstart.Application.Tests/SearchProfileParserTests.cs` (new), `tests/Coldstart.Infrastructure.Tests/GroqSearchProfileGeneratorTests.cs` (new)

**Interfaces produced:**
- `record SearchProfileRequest(string Niche, string Info)`
- `record SearchProfile(string Offer, string TargetKeywords, string BuyingSignals, string Disqualifiers)`
- `interface ISearchProfileGenerator { Task<SearchProfile?> GenerateAsync(SearchProfileRequest, CancellationToken) }`
- `static SearchProfileParser.Parse(string content) : SearchProfile?`

**Design note:** keywords/signals are stored as **newline-joined** strings (the harvest splits on `\n`/`,`). The LLM is asked for labelled sections (`OFFER`/`KEYWORDS`/`SIGNALS`/`DISQUALIFIERS`); the parser mirrors `EnrichmentParser`'s label-slicing. The Groq impl mirrors `GroqBriefPolisher` (no API key → returns null; best-effort try/catch).

- [ ] **Step 1: Write failing `SearchProfileParserTests.cs`**
```csharp
using Coldstart.Application.Customers;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class SearchProfileParserTests
{
    [Fact]
    public void Parse_ExtractsAllFour_AndJoinsListsWithNewlines()
    {
        // Arrange
        var content = """
            OFFER: scheduling software for dental clinics
            KEYWORDS: dentist, dental clinic, orthodontist
            SIGNALS: hiring, opening a new location
            DISQUALIFIERS: hospital groups; dental labs
            """;

        // Act
        var profile = SearchProfileParser.Parse(content);

        // Assert
        profile.Should().NotBeNull();
        profile!.Offer.Should().Be("scheduling software for dental clinics");
        profile.TargetKeywords.Should().Be("dentist\ndental clinic\northodontist");
        profile.BuyingSignals.Should().Be("hiring\nopening a new location");
        profile.Disqualifiers.Should().Be("hospital groups\ndental labs");
    }

    [Fact]
    public void Parse_WhenOfferOrKeywordsMissing_ReturnsNull()
    {
        // Arrange — no OFFER/KEYWORDS labels present
        var content = "SIGNALS: hiring\nDISQUALIFIERS: none";

        // Act
        var profile = SearchProfileParser.Parse(content);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public void Parse_ToleratesMissingOptionalSections()
    {
        // Arrange — only the required OFFER + KEYWORDS
        var content = "OFFER: X\nKEYWORDS: a, b";

        // Act
        var profile = SearchProfileParser.Parse(content);

        // Assert
        profile.Should().NotBeNull();
        profile!.BuyingSignals.Should().BeEmpty();
        profile.Disqualifiers.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run — expect FAIL.** Run: `dotnet test tests/Coldstart.Application.Tests --filter SearchProfileParserTests`

- [ ] **Step 3: Add the port + records** — `src/Coldstart.Application/Customers/ISearchProfileGenerator.cs`:
```csharp
namespace Coldstart.Application.Customers;

public sealed record SearchProfileRequest(string Niche, string Info);

// Search fields stored on the customer's CompanyProfile. Keyword/signal/disqualifier
// strings are newline-joined; the harvest splits them.
public sealed record SearchProfile(string Offer, string TargetKeywords, string BuyingSignals, string Disqualifiers);

public interface ISearchProfileGenerator
{
    Task<SearchProfile?> GenerateAsync(SearchProfileRequest request, CancellationToken ct);
}
```

- [ ] **Step 4: Add `SearchProfileParser.cs`**
```csharp
using System.Text.RegularExpressions;

namespace Coldstart.Application.Customers;

// Parses the LLM's labelled output into a SearchProfile. Requires OFFER + KEYWORDS;
// SIGNALS/DISQUALIFIERS are optional. Comma-or-newline lists are normalised to newline-joined.
public static class SearchProfileParser
{
    private static readonly string[] Labels = ["OFFER", "KEYWORDS", "SIGNALS", "DISQUALIFIERS"];

    public static SearchProfile? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var positions = new List<(int LabelStart, int ContentStart, string Label)>();
        foreach (var label in Labels)
        {
            var m = new Regex($@"^\s*{label}\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Multiline).Match(content);
            if (m.Success) positions.Add((m.Index, m.Index + m.Length, label));
        }
        positions.Sort((a, b) => a.LabelStart.CompareTo(b.LabelStart));

        var fields = new Dictionary<string, string>();
        for (var i = 0; i < positions.Count; i++)
        {
            var (_, start, label) = positions[i];
            var end = i + 1 < positions.Count ? positions[i + 1].LabelStart : content.Length;
            fields[label] = content[start..end].Trim();
        }

        string Field(string l) => fields.TryGetValue(l, out var v) ? v : "";
        static string JoinList(string raw) => string.Join('\n',
            raw.Split(['\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var offer = Field("OFFER");
        var keywords = JoinList(Field("KEYWORDS"));
        if (offer.Length == 0 || keywords.Length == 0) return null;   // required

        return new SearchProfile(offer, keywords, JoinList(Field("SIGNALS")), JoinList(Field("DISQUALIFIERS")));
    }
}
```

- [ ] **Step 5: Run — expect PASS** (SearchProfileParserTests).

- [ ] **Step 6: Write failing `GroqSearchProfileGeneratorTests.cs`** — stub `HttpClient` with a canned Groq response (mirrors how `GroqBriefPolisherTests` stubs HTTP):
```csharp
using System.Net;
using System.Text;
using Coldstart.Application.Customers;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class GroqSearchProfileGeneratorTests
{
    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(response);
    }

    private static HttpResponseMessage Groq(string content)
    {
        var json = $$"""{"choices":[{"message":{"content":{{System.Text.Json.JsonSerializer.Serialize(content)}}}}]}""";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task GenerateAsync_WithApiKey_ParsesTheProfile()
    {
        // Arrange
        var http = new HttpClient(new StubHandler(Groq("OFFER: X\nKEYWORDS: dentist, clinic\nSIGNALS: hiring\nDISQUALIFIERS: labs")));
        var opts = Options.Create(new LlmOptions { ApiKey = "k", Model = "m", BaseUrl = "https://api.groq.com/openai/v1/chat/completions" });
        var sut = new GroqSearchProfileGenerator(http, opts);

        // Act
        var profile = await sut.GenerateAsync(new SearchProfileRequest("dental clinics", "we sell scheduling"), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile!.TargetKeywords.Should().Be("dentist\nclinic");
    }

    [Fact]
    public async Task GenerateAsync_WithoutApiKey_ReturnsNull_AndMakesNoCall()
    {
        // Arrange — a handler that would throw if called
        var http = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var opts = Options.Create(new LlmOptions { ApiKey = "" });
        var sut = new GroqSearchProfileGenerator(http, opts);

        // Act
        var profile = await sut.GenerateAsync(new SearchProfileRequest("x", "y"), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }
}
```

- [ ] **Step 7: Run — expect FAIL** (type missing). Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter GroqSearchProfileGeneratorTests`

- [ ] **Step 8: Add `GroqSearchProfileGenerator.cs`**
```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Application.Customers;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Harvest;

internal sealed class GroqSearchProfileGenerator(HttpClient http, IOptions<LlmOptions> options) : ISearchProfileGenerator
{
    private const string System =
        "You turn a B2B company's niche and what they sell into a cold-outreach lead-search profile. " +
        "Return EXACTLY four labelled lines and nothing else:\n" +
        "OFFER: one crisp sentence describing what they sell, phrased as the offer to a prospect.\n" +
        "KEYWORDS: 8-12 comma-separated words/phrases that identify a prospect who fits (roles, industry terms).\n" +
        "SIGNALS: 5-8 comma-separated buying signals that mean 'reach out now' (e.g. hiring, expanding, raised).\n" +
        "DISQUALIFIERS: 2-4 comma-separated descriptions of who is NOT a fit. " +
        "Invent nothing beyond the input; be concrete.";

    private readonly LlmOptions _o = options.Value;

    public async Task<SearchProfile?> GenerateAsync(SearchProfileRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_o.ApiKey)) return null;
        var body = new
        {
            model = _o.Model,
            temperature = 0.3,
            messages = new[]
            {
                new { role = "system", content = System },
                new { role = "user", content = $"NICHE: {request.Niche}\nINFO: {request.Info}" },
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
            return string.IsNullOrWhiteSpace(content) ? null : SearchProfileParser.Parse(content);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
            or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        {
            return null;   // best-effort — the invite proceeds without a profile
        }
    }
}
```

- [ ] **Step 9: Register in Infrastructure DI** — in `AddInfrastructure`, next to the enricher/polisher registrations:
```csharp
        services.AddScoped<Application.Customers.ISearchProfileGenerator, Harvest.GroqSearchProfileGenerator>();
```

- [ ] **Step 10: Run — expect PASS.** Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter GroqSearchProfileGeneratorTests`

- [ ] **Step 11: Commit**
```bash
git add -A && git commit -m "feat(harvest): LLM search-profile generator from niche + info"
```

---

## Task 5: HarvestTargeting + per-customer harvest (RunForCustomerAsync)

Split targeting from the global source config so a harvest can run for one customer with their own keywords, while the console path is behaviour-stable.

**Files:**
- Create: `src/Coldstart.Application/Harvest/HarvestTargeting.cs`
- Modify: `src/Coldstart.Application/Harvest/IHarvestLeads.cs`, `src/Coldstart.Application/Harvest/HarvestLeads.cs`
- Test: `tests/Coldstart.Application.Tests/HarvestLeadsTests.cs`

**Interfaces produced:**
- `record HarvestTargeting(string Offer, IReadOnlyList<string> TargetKeywords, IReadOnlyList<string> BuyingSignals, IReadOnlyList<string> Disqualifiers, string SenderName, string SenderTitle, double MinScore, int MaxEnrichPerRun)` with `bool HasKeywords`, `static FromNiche(NicheOptions)`, `static FromProfile(CompanyProfile, string customerName)`
- `IHarvestLeads.RunForCustomerAsync(Guid customerId, HarvestTargeting targeting, CancellationToken)`

**Design note:** `RunAsync` (console) keeps resolving the target by `NicheOptions.TargetCustomerName` and builds its `SenderContext` from that customer's profile — unchanged. Both paths funnel into a private `RunPipelineAsync(CustomerId, HarvestTargeting, SenderContext, ct)`. A per-run `NicheOptions` is built from the targeting plus the injected global `HackerNews`/`Funding` source config, so scoring/enrichment stay keyword-driven.

- [ ] **Step 1: Add `HarvestTargeting.cs`**
```csharp
using Coldstart.Domain;

namespace Coldstart.Application.Harvest;

public sealed record HarvestTargeting(
    string Offer,
    IReadOnlyList<string> TargetKeywords,
    IReadOnlyList<string> BuyingSignals,
    IReadOnlyList<string> Disqualifiers,
    string SenderName,
    string SenderTitle,
    double MinScore,
    int MaxEnrichPerRun)
{
    public bool HasKeywords => TargetKeywords.Count > 0 || BuyingSignals.Count > 0;

    public static HarvestTargeting FromNiche(NicheOptions n) => new(
        n.Offer, n.TargetKeywords, n.BuyingSignals, n.Disqualifiers,
        n.SenderName, n.SenderTitle, n.MinScore, n.MaxEnrichPerRun);

    public static HarvestTargeting FromProfile(CompanyProfile profile, string customerName) => new(
        profile.Offer ?? "",
        Split(profile.TargetKeywords),
        Split(profile.BuyingSignals),
        Split(profile.Disqualifiers),
        SenderName: string.IsNullOrWhiteSpace(profile.CompanyName) ? customerName : profile.CompanyName!,
        SenderTitle: "",
        MinScore: 0.4,
        MaxEnrichPerRun: 30);

    private static IReadOnlyList<string> Split(string? joined) =>
        string.IsNullOrWhiteSpace(joined)
            ? []
            : joined.Split(['\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
```

- [ ] **Step 2: Extend `IHarvestLeads.cs`**
```csharp
namespace Coldstart.Application.Harvest;

public interface IHarvestLeads
{
    Task<HarvestReport> RunAsync(CancellationToken ct);
    Task<HarvestReport> RunForCustomerAsync(Guid customerId, HarvestTargeting targeting, CancellationToken ct);
}
```

- [ ] **Step 3: Write failing `HarvestLeadsTests` case** — append (match the file's existing fakes for `ILeadSource`/`ILeadEnricher`/repos; this test drives the new entry point):
```csharp
    [Fact]
    public async Task RunForCustomerAsync_SavesEnrichedLeadsToTheGivenCustomer_UsingTargetingKeywords()
    {
        // Arrange — one source item that matches the targeting keyword; enricher returns a YES
        var cid = Guid.NewGuid();
        var customer = Customer.Create("Acme");
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetAsync(new CustomerId(cid), Arg.Any<CancellationToken>()).Returns(customer);

        var item = new RawItem("hackernews", "1", "hiring a dentist", "we need a dentist now", "acme", "https://x/1", "2026-07-23");
        var source = Substitute.For<ILeadSource>();
        source.Enabled.Returns(true);
        source.BaseScore.Returns(0.0);
        source.ScoreWeight.Returns(1.0);
        source.FetchAsync(Arg.Any<CancellationToken>()).Returns(new[] { item });

        var leads = Substitute.For<ILeadRepository>();
        leads.ExistsByFingerprintAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var enricher = Substitute.For<ILeadEnricher>();
        enricher.EnrichAsync(Arg.Any<RawItem>(), Arg.Any<NicheOptions>(), Arg.Any<SignalResult>(), Arg.Any<SenderContext>(), Arg.Any<CancellationToken>())
            .Returns(new EnrichmentResult(Fit.Yes, "sum", "subj", "draft", "Acme"));

        var globalNiche = new NicheOptions();   // provides HackerNews/Funding source config
        var scope = Substitute.For<ITenantScope>();
        var sut = new HarvestLeads(new[] { source }, enricher, leads, customers, globalNiche, scope);
        var targeting = new HarvestTargeting("offer", ["dentist"], ["hiring"], [], "Acme", "", 0.4, 30);

        // Act
        var report = await sut.RunForCustomerAsync(cid, targeting, CancellationToken.None);

        // Assert — the run scoped to the customer and saved the lead under its id
        scope.Received().SetCurrent(cid);
        report.Saved.Should().Be(1);
        await leads.Received().AddAsync(Arg.Is<Lead>(l => l.CustomerId.Value == cid && l.Company == "Acme"), Arg.Any<CancellationToken>());
    }
```
Add any missing `using` (`Coldstart.Application.Harvest;`, `Coldstart.Application.Ports;`, `Coldstart.Domain;`).

- [ ] **Step 4: Run — expect FAIL** (method missing). Run: `dotnet test tests/Coldstart.Application.Tests --filter HarvestLeadsTests`

- [ ] **Step 5: Refactor `HarvestLeads.cs`** — keep `ResolveTargetCustomerAsync`; funnel both entry points through a shared pipeline:
```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Harvest;

internal sealed class HarvestLeads(
    IEnumerable<ILeadSource> sources,
    ILeadEnricher enricher,
    ILeadRepository leads,
    ICustomerRepository customers,
    NicheOptions niche,
    ITenantScope tenantScope) : IHarvestLeads
{
    public async Task<HarvestReport> RunAsync(CancellationToken ct)
    {
        // Console path: resolve the configured target tenant, build sender from ITS profile (unchanged).
        var target = await ResolveTargetCustomerAsync(ct);
        tenantScope.SetCurrent(target.Id.Value);
        var sender = new SenderContext(target.Profile.Brief, target.Profile.MentionSourceTool);
        return await RunPipelineAsync(target.Id, HarvestTargeting.FromNiche(niche), sender, ct);
    }

    public async Task<HarvestReport> RunForCustomerAsync(Guid customerId, HarvestTargeting targeting, CancellationToken ct)
    {
        var cid = new CustomerId(customerId);
        tenantScope.SetCurrent(customerId);
        var customer = await customers.GetAsync(cid, ct);
        var sender = customer is null
            ? SenderContext.None
            : new SenderContext(customer.Profile.Brief, customer.Profile.MentionSourceTool);
        return await RunPipelineAsync(cid, targeting, sender, ct);
    }

    private async Task<HarvestReport> RunPipelineAsync(CustomerId cid, HarvestTargeting targeting, SenderContext sender, CancellationToken ct)
    {
        var runNiche = BuildRunNiche(targeting);
        int harvested = 0, scoredIn = 0, deduped = 0, enriched = 0, saved = 0, rejected = 0;

        var candidates = new List<(RawItem item, SignalResult signal)>();
        var seen = new HashSet<string>();
        foreach (var source in sources.Where(s => s.Enabled))
        {
            IReadOnlyList<RawItem> items;
            try { items = await source.FetchAsync(ct); }
            catch { continue; }
            foreach (var item in items)
            {
                harvested++;
                var signal = SignalScorer.Score(item, runNiche, source.BaseScore, source.ScoreWeight);
                if (signal.Score < runNiche.MinScore) continue;
                scoredIn++;
                if (await leads.ExistsByFingerprintAsync(item.Fingerprint, ct)) { deduped++; continue; }
                if (item.Fingerprint.Length != 0 && !seen.Add(item.Fingerprint)) { deduped++; continue; }
                candidates.Add((item, signal));
            }
        }

        foreach (var (item, signal) in candidates.OrderByDescending(c => c.signal.Score).Take(runNiche.MaxEnrichPerRun))
        {
            var result = await enricher.EnrichAsync(item, runNiche, signal, sender, ct);
            if (result is null) continue;
            enriched++;
            var company = string.IsNullOrWhiteSpace(result.Company) || result.Company == "-" ? item.Author : result.Company;
            var lead = Lead.CreateHarvested(cid, company, result.Fit, result.Subject, result.Draft,
                item.Source, item.Url, item.Fingerprint, signal.Score, string.Join(",", signal.Matched), DateTimeOffset.UtcNow);
            if (result.Fit == Fit.No)
            {
                lead.Reject();
                await leads.AddAsync(lead, ct);
                rejected++;
                continue;
            }
            await leads.AddAsync(lead, ct);
            saved++;
        }

        return new HarvestReport(harvested, scoredIn, deduped, enriched, saved, rejected);
    }

    // Per-run options: per-customer targeting + the injected global source config.
    private NicheOptions BuildRunNiche(HarvestTargeting t) => new()
    {
        Offer = t.Offer,
        SenderName = t.SenderName,
        SenderTitle = t.SenderTitle,
        TargetKeywords = t.TargetKeywords.ToList(),
        BuyingSignals = t.BuyingSignals.ToList(),
        Disqualifiers = t.Disqualifiers.ToList(),
        MinScore = t.MinScore,
        MaxEnrichPerRun = t.MaxEnrichPerRun,
        HackerNews = niche.HackerNews,
        Funding = niche.Funding,
    };

    private async Task<Customer> ResolveTargetCustomerAsync(CancellationToken ct)
    {
        var existing = await customers.GetByNameAsync(niche.TargetCustomerName, ct);
        if (existing is not null) return existing;
        var created = Customer.Create(niche.TargetCustomerName);
        await customers.AddAsync(created, ct);
        return created;
    }
}
```

- [ ] **Step 6: Run — expect PASS** (new test + existing `HarvestLeadsTests` unchanged, since `RunAsync`'s sender still comes from the resolved target). Run: `dotnet test tests/Coldstart.Application.Tests --filter HarvestLeadsTests`

- [ ] **Step 7: Commit**
```bash
git add -A && git commit -m "feat(harvest): per-customer RunForCustomerAsync with HarvestTargeting"
```

---

## Task 6: Background harvest queue + hosted service + tenant fallback

An in-process channel drained one job at a time by a `BackgroundService`. Also fixes tenant resolution for background scopes (no HttpContext).

**Files:**
- Create: `src/Coldstart.Application/Harvest/IHarvestQueue.cs`, `src/Coldstart.Infrastructure/Harvest/HarvestQueue.cs`, `src/Coldstart.Infrastructure/Harvest/HarvestQueueService.cs`
- Modify: `src/Coldstart.Infrastructure/Tenancy/HttpTenantContext.cs`, `src/Coldstart.Infrastructure/DependencyInjection.cs`, `src/Coldstart.Infrastructure/Coldstart.Infrastructure.csproj`, `tests/Coldstart.Infrastructure.Tests/HttpTenantContextTests.cs`
- Test: `tests/Coldstart.Infrastructure.Tests/HarvestQueueTests.cs` (new), `tests/Coldstart.Infrastructure.Tests/HttpTenantContextTests.cs`

**Interfaces produced:**
- `record HarvestJob(Guid CustomerId)`, `interface IHarvestQueue { void Enqueue(HarvestJob) }`
- `internal HarvestQueue : IHarvestQueue` with `ChannelReader<HarvestJob> Reader`
- `internal HarvestQueueService : BackgroundService`
- `IServiceCollection AddHarvestQueue(this IServiceCollection)`
- `HttpTenantContext` falls back to the scoped `MutableTenantContext` when no request/claim is present.

**Design note (tenant fallback):** In Web, `ITenantContext` resolves to `HttpTenantContext`, but a `BackgroundService` scope has no `HttpContext`, so `customer_id` claim reads return null and every filtered read fails closed. Give `HttpTenantContext` a `MutableTenantContext` fallback: request claim wins; otherwise use the scoped `MutableTenantContext` that `ITenantScope.SetCurrent` writes (both resolve the same scoped instance). This makes the background job's `SetCurrent(customerId)` take effect.

- [ ] **Step 1: Add port** — `src/Coldstart.Application/Harvest/IHarvestQueue.cs`:
```csharp
namespace Coldstart.Application.Harvest;

public sealed record HarvestJob(Guid CustomerId);

public interface IHarvestQueue
{
    void Enqueue(HarvestJob job);
}
```

- [ ] **Step 2: Write failing `HarvestQueueTests.cs`**
```csharp
using Coldstart.Application.Harvest;
using Coldstart.Infrastructure.Harvest;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class HarvestQueueTests
{
    [Fact]
    public async Task Enqueue_MakesJobsReadable_InFifoOrder()
    {
        // Arrange
        var queue = new HarvestQueue();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        // Act
        queue.Enqueue(new HarvestJob(a));
        queue.Enqueue(new HarvestJob(b));

        // Assert — both readable, in order
        var read = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var job = await queue.Reader.ReadAsync(CancellationToken.None);
            read.Add(job.CustomerId);
        }
        read.Should().Equal(a, b);
    }
}
```

- [ ] **Step 3: Run — expect FAIL.** Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter HarvestQueueTests`

- [ ] **Step 4: Add `HarvestQueue.cs`**
```csharp
using System.Threading.Channels;
using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

internal sealed class HarvestQueue : IHarvestQueue
{
    private readonly Channel<HarvestJob> _channel = Channel.CreateUnbounded<HarvestJob>();

    public ChannelReader<HarvestJob> Reader => _channel.Reader;

    public void Enqueue(HarvestJob job) => _channel.Writer.TryWrite(job);
}
```

- [ ] **Step 5: Run — expect PASS** (HarvestQueueTests).

- [ ] **Step 6: Add the `Microsoft.Extensions.Hosting.Abstractions` package** to `src/Coldstart.Infrastructure/Coldstart.Infrastructure.csproj` (for `BackgroundService` + `AddHostedService`). Add inside the main `<ItemGroup>` of `<PackageReference>`s:
```xml
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.9" />
```
Run `dotnet restore` and confirm it builds: `dotnet build src/Coldstart.Infrastructure`.

- [ ] **Step 7: Add `HarvestQueueService.cs`**
```csharp
using Coldstart.Application.Harvest;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coldstart.Infrastructure.Harvest;

// Drains the harvest queue one job at a time (sequential → stays under the Groq rate limit).
// Each job runs in its own DI scope with the tenant set to the job's customer.
internal sealed class HarvestQueueService(
    HarvestQueue queue,
    IServiceScopeFactory scopes,
    ILogger<HarvestQueueService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try { await RunJobAsync(job, stoppingToken); }
            catch (Exception ex) { log.LogError(ex, "Harvest job for customer {CustomerId} failed", job.CustomerId); }
        }
    }

    private async Task RunJobAsync(HarvestJob job, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ITenantScope>().SetCurrent(job.CustomerId);

        var customers = sp.GetRequiredService<ICustomerRepository>();
        var customer = await customers.GetAsync(new CustomerId(job.CustomerId), ct);
        if (customer is null) return;

        var targeting = HarvestTargeting.FromProfile(customer.Profile, customer.Name);
        if (!targeting.HasKeywords)
        {
            // No usable search profile (generation failed at invite) → mark failed, no bogus leads.
            customer.MarkHarvest(HarvestStatus.Failed, DateTimeOffset.UtcNow);
            await customers.UpdateAsync(customer, ct);
            return;
        }

        customer.MarkHarvest(HarvestStatus.Running, null);
        await customers.UpdateAsync(customer, ct);
        try
        {
            await sp.GetRequiredService<IHarvestLeads>().RunForCustomerAsync(job.CustomerId, targeting, ct);
            customer.MarkHarvest(HarvestStatus.Done, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Harvest run for customer {CustomerId} failed", job.CustomerId);
            customer.MarkHarvest(HarvestStatus.Failed, DateTimeOffset.UtcNow);
        }
        await customers.UpdateAsync(customer, ct);
    }
}
```

- [ ] **Step 8: Modify `HttpTenantContext.cs`** — add the `MutableTenantContext` fallback. **Preserve the existing claim-reading expression exactly**; only add the constructor parameter and the fallback tail. The result should read like:
```csharp
using Coldstart.Application.Ports;
using Microsoft.AspNetCore.Http;

namespace Coldstart.Infrastructure.Tenancy;

// Request claim wins; with no HttpContext/claim (e.g. a background harvest scope) fall back to the
// scoped MutableTenantContext that ITenantScope.SetCurrent writes.
internal sealed class HttpTenantContext(IHttpContextAccessor accessor, MutableTenantContext fallback) : ITenantContext
{
    public Guid? CustomerId
    {
        get
        {
            // KEEP the repo's existing claim lookup here (the "customer_id" claim via AppUserClaimsPrincipalFactory).
            var claim = accessor.HttpContext?.User?.FindFirst(Identity.AppUserClaimsPrincipalFactory.CustomerIdClaim)?.Value;
            if (Guid.TryParse(claim, out var id)) return id;
            return fallback.CustomerId;
        }
    }
}
```
(If the current file references the claim by a string literal rather than the `AppUserClaimsPrincipalFactory.CustomerIdClaim` constant, keep whatever it currently uses.)

- [ ] **Step 9: Update `HttpTenantContextTests.cs`** — the ctor now needs a `MutableTenantContext`. Update existing constructions to pass `new MutableTenantContext()` and add a fallback test:
```csharp
    [Fact]
    public void CustomerId_WithNoHttpContext_FallsBackToMutableTenantContext()
    {
        // Arrange — no HttpContext, but a tenant set on the fallback (as a background job would)
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var fallback = new MutableTenantContext();
        var id = Guid.NewGuid();
        ((Coldstart.Application.Ports.ITenantScope)fallback).SetCurrent(id);
        var sut = new HttpTenantContext(accessor, fallback);

        // Act / Assert
        sut.CustomerId.Should().Be(id);
    }
```
Add `using NSubstitute;`, `using Microsoft.AspNetCore.Http;`, `using Coldstart.Infrastructure.Tenancy;` if missing.

- [ ] **Step 10: Add `AddHarvestQueue`** to `Infrastructure/DependencyInjection.cs` (a Web-only extension, like `AddHttpTenantResolution`):
```csharp
    // Web-only: the in-process harvest queue + its background worker.
    public static IServiceCollection AddHarvestQueue(this IServiceCollection services)
    {
        services.AddSingleton<Harvest.HarvestQueue>();
        services.AddSingleton<Application.Harvest.IHarvestQueue>(sp => sp.GetRequiredService<Harvest.HarvestQueue>());
        services.AddHostedService<Harvest.HarvestQueueService>();
        return services;
    }
```

- [ ] **Step 11: Run Infra tests — expect PASS** (`HarvestQueueTests` + `HttpTenantContextTests`, and the existing tenant/isolation tests still green). Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "HarvestQueueTests|HttpTenantContextTests"`

- [ ] **Step 12: Commit**
```bash
git add -A && git commit -m "feat(harvest): in-process harvest queue + hosted worker + background tenant fallback"
```

---

## Task 7: Provisioning with a company-profile seed

**Files:**
- Create: `src/Coldstart.Application/Customers/CompanyProfileSeed.cs`
- Modify: `src/Coldstart.Application/Customers/IProvisionCustomer.cs`, `src/Coldstart.Application/Customers/ProvisionCustomer.cs`
- Test: `tests/Coldstart.Application.Tests/ProvisionCustomerTests.cs`

**Interfaces produced:**
- `record CompanyProfileSeed(string? Niche, string? Info, SearchProfile? SearchProfile)`
- `IProvisionCustomer.ProvisionAsync(string customerName, CompanyProfileSeed seed, CancellationToken)` — populates the profile (Niche, Brief=Info, search fields) and marks `HarvestStatus.Pending`. The existing name-only overload is unchanged.

- [ ] **Step 1: Write failing `ProvisionCustomerTests` case** — append (file already tests the name-only path):
```csharp
    [Fact]
    public async Task ProvisionAsync_WithSeed_StoresProfileAndMarksHarvestPending()
    {
        // Arrange
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetByNameAsync("Acme", Arg.Any<CancellationToken>()).Returns((Customer?)null);
        Customer? saved = null;
        await customers.AddAsync(Arg.Do<Customer>(c => saved = c), Arg.Any<CancellationToken>());
        var sut = new ProvisionCustomer(customers);
        var seed = new CompanyProfileSeed("dental clinics", "we sell scheduling",
            new SearchProfile("scheduling", "dentist\nclinic", "hiring", "labs"));

        // Act
        await sut.ProvisionAsync("Acme", seed, CancellationToken.None);

        // Assert
        saved.Should().NotBeNull();
        saved!.Profile.Niche.Should().Be("dental clinics");
        saved.Profile.Brief.Should().Be("we sell scheduling");
        saved.Profile.TargetKeywords.Should().Be("dentist\nclinic");
        saved.Profile.Offer.Should().Be("scheduling");
        saved.HarvestStatus.Should().Be(HarvestStatus.Pending);
    }

    [Fact]
    public async Task ProvisionAsync_WithSeed_WhenNameExists_ReturnsExistingWithoutOverwriting()
    {
        // Arrange
        var existing = Customer.Create("Acme");
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetByNameAsync("Acme", Arg.Any<CancellationToken>()).Returns(existing);
        var sut = new ProvisionCustomer(customers);

        // Act
        var id = await sut.ProvisionAsync("Acme", new CompanyProfileSeed("x", "y", null), CancellationToken.None);

        // Assert
        id.Should().Be(existing.Id.Value);
        await customers.DidNotReceive().AddAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }
```
Add `using Coldstart.Domain;` if missing.

- [ ] **Step 2: Run — expect FAIL.** Run: `dotnet test tests/Coldstart.Application.Tests --filter ProvisionCustomerTests`

- [ ] **Step 3: Add `CompanyProfileSeed.cs`**
```csharp
namespace Coldstart.Application.Customers;

public sealed record CompanyProfileSeed(string? Niche, string? Info, SearchProfile? SearchProfile);
```

- [ ] **Step 4: Extend `IProvisionCustomer.cs`**
```csharp
namespace Coldstart.Application.Customers;

public interface IProvisionCustomer
{
    Task<Guid> ProvisionAsync(string customerName, CancellationToken ct);
    Task<Guid> ProvisionAsync(string customerName, CompanyProfileSeed seed, CancellationToken ct);
}
```

- [ ] **Step 5: Extend `ProvisionCustomer.cs`** — add the seed overload; keep the existing method:
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

    public async Task<Guid> ProvisionAsync(string customerName, CompanyProfileSeed seed, CancellationToken ct)
    {
        var existing = await customers.GetByNameAsync(customerName, ct);
        if (existing is not null) return existing.Id.Value;

        var customer = Customer.Create(customerName);
        var sp = seed.SearchProfile;
        customer.UpdateProfile(new CompanyProfile(
            CompanyName: customerName, Address: null, VatNumber: null, KvkNumber: null,
            Niche: seed.Niche, Website: null, ContactEmail: null,
            Brief: seed.Info, MentionSourceTool: false,
            Offer: sp?.Offer, TargetKeywords: sp?.TargetKeywords,
            BuyingSignals: sp?.BuyingSignals, Disqualifiers: sp?.Disqualifiers));
        customer.MarkHarvest(HarvestStatus.Pending, null);
        await customers.AddAsync(customer, ct);
        return customer.Id.Value;
    }
}
```

- [ ] **Step 6: Run — expect PASS.** Run: `dotnet test tests/Coldstart.Application.Tests --filter ProvisionCustomerTests`

- [ ] **Step 7: Commit**
```bash
git add -A && git commit -m "feat(customers): provisioning seeds profile + search fields + pending harvest"
```

---

## Task 8: Inquiry repository — recent list + get-by-id

For "invite from an inquiry": list recent inquiries and load one to prefill the invite.

**Files:**
- Modify: `src/Coldstart.Application/Ports/IInquiryRepository.cs`, `src/Coldstart.Infrastructure/Repositories/InquiryRepository.cs`
- Test: `tests/Coldstart.Infrastructure.Tests/InquiryRepositoryTests.cs`

**Interfaces produced:**
- `IInquiryRepository.RecentAsync(int max, CancellationToken) : Task<IReadOnlyList<Inquiry>>`
- `IInquiryRepository.GetAsync(InquiryId id, CancellationToken) : Task<Inquiry?>`

- [ ] **Step 1: Write failing `InquiryRepositoryTests` cases** — append (file uses `IClassFixture<PostgresFixture>`):
```csharp
    [Fact]
    public async Task RecentAsync_ReturnsNewestFirst_UpToMax()
    {
        // Arrange — three inquiries with increasing CreatedAt
        var older = Inquiry.Create($"A-{Guid.NewGuid():N}", new EmailAddress("a@x.com"), "n", "s", null, DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = Inquiry.Create($"B-{Guid.NewGuid():N}", new EmailAddress("b@x.com"), "n", "s", null, DateTimeOffset.UtcNow);
        await using (var seed = fixture.NewDbContext())
        {
            seed.Inquiries.AddRange(older, newer);
            await seed.SaveChangesAsync();
        }

        // Act
        await using var read = fixture.NewDbContext();
        var recent = await new InquiryRepository(read).RecentAsync(50, CancellationToken.None);

        // Assert — includes both, newest before older
        var idxNewer = recent.ToList().FindIndex(i => i.Id == newer.Id);
        var idxOlder = recent.ToList().FindIndex(i => i.Id == older.Id);
        idxNewer.Should().BeGreaterThanOrEqualTo(0);
        idxOlder.Should().BeGreaterThan(idxNewer);
    }

    [Fact]
    public async Task GetAsync_ReturnsTheInquiry_ById()
    {
        // Arrange
        var inquiry = Inquiry.Create($"C-{Guid.NewGuid():N}", new EmailAddress("c@x.com"), "niche", "sells", "msg", DateTimeOffset.UtcNow);
        await using (var seed = fixture.NewDbContext())
        {
            seed.Inquiries.Add(inquiry);
            await seed.SaveChangesAsync();
        }

        // Act
        await using var read = fixture.NewDbContext();
        var found = await new InquiryRepository(read).GetAsync(inquiry.Id, CancellationToken.None);

        // Assert
        found.Should().NotBeNull();
        found!.Niche.Should().Be("niche");
    }
```
Add `using Coldstart.Domain;` and `using Coldstart.Infrastructure.Repositories;` if missing.

- [ ] **Step 2: Run — expect FAIL.** Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter InquiryRepositoryTests`

- [ ] **Step 3: Extend `IInquiryRepository.cs`**
```csharp
using Coldstart.Domain;

namespace Coldstart.Application.Ports;

public interface IInquiryRepository
{
    Task AddAsync(Inquiry inquiry, CancellationToken ct);
    Task<IReadOnlyList<Inquiry>> RecentAsync(int max, CancellationToken ct);
    Task<Inquiry?> GetAsync(InquiryId id, CancellationToken ct);
}
```

- [ ] **Step 4: Extend `InquiryRepository.cs`**
```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Infrastructure.Repositories;

internal sealed class InquiryRepository(ColdstartDbContext db) : IInquiryRepository
{
    public async Task AddAsync(Inquiry inquiry, CancellationToken ct)
    {
        await db.Inquiries.AddAsync(inquiry, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Inquiry>> RecentAsync(int max, CancellationToken ct) =>
        await db.Inquiries.OrderByDescending(i => i.CreatedAt).Take(max).ToListAsync(ct);

    public async Task<Inquiry?> GetAsync(InquiryId id, CancellationToken ct) =>
        await db.Inquiries.FirstOrDefaultAsync(i => i.Id == id, ct);
}
```
(If `InquiryId` has no public `(Guid)` ctor, the Web layer in Task 10 will use whatever construction the domain exposes; `GetAsync` here takes the value object directly so it is agnostic.)

- [ ] **Step 5: Run — expect PASS.** Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter InquiryRepositoryTests`

- [ ] **Step 6: Commit**
```bash
git add -A && git commit -m "feat(inquiries): recent list + get-by-id for invite prefill"
```

---

## Task 9: Web host wiring + seeder plan + test fakes

Register the config-bound options, the harvest queue/worker, seed the operator tenant as `Full`, and add the test doubles the Web feature tests need (so invites don't fire live HTTP in tests).

**Files:**
- Modify: `src/Coldstart.Web/Program.cs`, `src/Coldstart.Web/appsettings.json`, `src/Coldstart.Web/appsettings.Development.json`, `src/Coldstart.Web/Identity/IdentitySeeder.cs`
- Create: `tests/Coldstart.Web.Tests/FakeHarvestQueue.cs`, `tests/Coldstart.Web.Tests/FakeSearchProfileGenerator.cs`
- Modify: `tests/Coldstart.Web.Tests/ColdstartWebFactory.cs`, `tests/Coldstart.Web.Tests/IdentitySeedTests.cs`

**Interfaces consumed:** `NicheOptions`, `QuotaOptions`, `AddHarvestQueue()`, `ISearchProfileGenerator`, `IHarvestQueue`/`HarvestJob`, `ICustomerRepository`, `Customer.SetPlan`, `CustomerPlan`.

**Design note:** existing Web test classes add a handful of leads per customer; to keep them unaffected the shared factory sets `Quota:FreeLeadCount` very high. Task 11's foil tests use a dedicated `FoilWebFactory` with a small quota. The shared factory also replaces `IHarvestQueue` (record-only fake, never runs a harvest) and `ISearchProfileGenerator` (canned profile) so invites are fast and offline. The real `HarvestQueueService` still registers but idles (nothing writes to the real channel).

- [ ] **Step 1: Wire options + queue in `Program.cs`** — after the existing `builder.Services.Configure<...>` block (around the OutreachMailbox line), add:
```csharp
builder.Services.AddSingleton(_ =>
{
    var n = new Coldstart.Application.Harvest.NicheOptions();
    builder.Configuration.GetSection("Niche").Bind(n);
    return n;
});
builder.Services.AddSingleton(_ =>
{
    var q = new Coldstart.Application.Leads.QuotaOptions();
    builder.Configuration.GetSection("Quota").Bind(q);
    return q;
});
builder.Services.AddHarvestQueue();
```
(`NicheOptions` here overrides the Infrastructure `TryAddSingleton` default; `QuotaOptions` overrides the Application default. `Llm` is already configured via the existing `Configure<LlmOptions>` line.)

- [ ] **Step 2: Add config sections** — in `src/Coldstart.Web/appsettings.json`, add a `"Quota"` block and an `"Llm"` block (structural defaults; API key comes from env/user-secrets):
```json
  "AllowedHosts": "*",
  "Quota": { "FreeLeadCount": 3 },
  "Llm": { "Provider": "groq", "Model": "llama-3.3-70b-versatile" },
  "Smtp": {
    "Username": "",
    "Password": ""
  }
```
In `src/Coldstart.Web/appsettings.Development.json`, add a `"Niche"` section (source config so the Web-host harvest has feeds; copy the Harvester's `HackerNews`/`Funding` values) plus `"Quota"`:
```json
  "ConnectionStrings": {
    "Coldstart": "Host=localhost;Port=5432;Database=coldstart;Username=coldstart;Password=localdev"
  },
  "Quota": { "FreeLeadCount": 3 },
  "Niche": {
    "HackerNews": { "Enabled": true, "Queries": ["hiring sales development","hiring BDR","head of growth","outbound sales","we are hiring sales"], "MaxPerQuery": 30, "LookbackHours": 336, "ScoreWeight": 0.7 },
    "Funding": { "Enabled": true, "Feeds": ["https://techcrunch.com/category/venture/feed/","https://techcrunch.com/category/startups/feed/","https://tech.eu/feed/","https://www.eu-startups.com/feed/","https://news.crunchbase.com/feed/","https://techfundingnews.com/feed/"], "LookbackDays": 45, "MaxPerFeed": 40, "RequireFundingTerms": true, "BaseScore": 0.5 }
  }
```
(Per-customer harvests supply keywords/signals/offer from the customer's profile; only the source config lives here.)

- [ ] **Step 3: Seed the operator tenant as `Full`** — in `IdentitySeeder.SeedAsync`, after the `if (admin.CustomerId is null) { ... }` block, add (idempotent):
```csharp
        // The operator's own Coldstart tenant is a full-access account — no free-tier foil.
        var customers = services.GetRequiredService<Application.Ports.ICustomerRepository>();
        var coldstart = await customers.GetByNameAsync("Coldstart", ct);
        if (coldstart is not null && coldstart.Plan != Domain.CustomerPlan.Full)
        {
            coldstart.SetPlan(Domain.CustomerPlan.Full);
            await customers.UpdateAsync(coldstart, ct);
        }
```
Add `using Coldstart.Application.Ports;` and `using Coldstart.Domain;` (or use the fully-qualified names shown).

- [ ] **Step 4: Add `FakeHarvestQueue.cs`**
```csharp
using Coldstart.Application.Harvest;

namespace Coldstart.Web.Tests;

// Records enqueued jobs without running a harvest (keeps invites offline in tests).
public sealed class FakeHarvestQueue : IHarvestQueue
{
    public List<HarvestJob> Jobs { get; } = [];

    public void Enqueue(HarvestJob job) => Jobs.Add(job);
}
```

- [ ] **Step 5: Add `FakeSearchProfileGenerator.cs`**
```csharp
using Coldstart.Application.Customers;

namespace Coldstart.Web.Tests;

// Returns a canned profile so invite tests can assert what gets stored, with no HTTP.
public sealed class FakeSearchProfileGenerator : ISearchProfileGenerator
{
    public SearchProfile? Result { get; set; } =
        new("scheduling software", "dentist\nclinic", "hiring\nexpanding", "hospital groups");

    public SearchProfileRequest? LastRequest { get; private set; }

    public Task<SearchProfile?> GenerateAsync(SearchProfileRequest request, CancellationToken ct)
    {
        LastRequest = request;
        return Task.FromResult(Result);
    }
}
```

- [ ] **Step 6: Register fakes + high quota in `ColdstartWebFactory.cs`** — add fields and registrations. Add near the other fakes:
```csharp
    public FakeHarvestQueue HarvestQueue { get; } = new();
    public FakeSearchProfileGenerator SearchProfiles { get; } = new();
```
In `ConfigureWebHost`, add the quota setting before `ConfigureServices`:
```csharp
        builder.UseSetting("Quota:FreeLeadCount", "100000");   // effectively no foil for the shared factory
```
Inside the `ConfigureServices` lambda, add:
```csharp
            services.RemoveAll<Coldstart.Application.Harvest.IHarvestQueue>();
            services.AddSingleton<Coldstart.Application.Harvest.IHarvestQueue>(HarvestQueue);
            services.RemoveAll<Coldstart.Application.Customers.ISearchProfileGenerator>();
            services.AddSingleton<Coldstart.Application.Customers.ISearchProfileGenerator>(SearchProfiles);
```
(Keep the existing removals/additions. The high `Quota:FreeLeadCount` is bound by `Program.cs`'s `GetSection("Quota")`.)

- [ ] **Step 7: Add a seeder assertion to `IdentitySeedTests.cs`**
```csharp
    [Fact]
    public async Task Seed_MakesTheOperatorColdstartTenantFullPlan()
    {
        // Arrange / Act — the app has already seeded on startup
        using var scope = factory.Services.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<Coldstart.Application.Ports.ICustomerRepository>();

        // Assert
        var coldstart = await customers.GetByNameAsync("Coldstart", CancellationToken.None);
        coldstart.Should().NotBeNull();
        coldstart!.Plan.Should().Be(Coldstart.Domain.CustomerPlan.Full);
    }
```
(Match the file's existing fixture field name — likely `factory`. Add any missing `using`.)

- [ ] **Step 8: Run the Web suite — expect PASS** (all existing Web tests unaffected by the high quota; new seeder test green). Run: `dotnet test tests/Coldstart.Web.Tests`

- [ ] **Step 9: Commit**
```bash
git add -A && git commit -m "chore(web): wire niche/quota options + harvest queue; seed operator tenant Full; test fakes"
```

---

## Task 10: Invite flow — niche/info, invite-from-inquiry, generate + provision + enqueue

**Files:**
- Modify: `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml.cs`, `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml`
- Test: `tests/Coldstart.Web.Tests/OnboardingInviteTests.cs` (new)

**Interfaces consumed:** `ISearchProfileGenerator`/`SearchProfileRequest`, `IProvisionCustomer.ProvisionAsync(name, seed, ct)` + `CompanyProfileSeed`, `IHarvestQueue`/`HarvestJob`, `IInquiryRepository.RecentAsync`/`GetAsync`, `InquiryId`.

**Design note:** Company is always typed by the admin (an inquiry has no company field). Selecting an inquiry prefills Email/Niche/Info via `GET /admin?useInquiry={id}`. Profile generation is one quick LLM call wrapped so failure never blocks the invite; the invite sends the email immediately and enqueues the harvest last.

- [ ] **Step 1: Write failing `OnboardingInviteTests.cs`**
```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Web;
using Microsoft.AspNetCore.Identity;
using Coldstart.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class OnboardingInviteTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Invite_WithNicheAndInfo_StoresProfile_SendsEmail_AndEnqueuesHarvest()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
        var invitee = $"newco-{Guid.NewGuid():N}@test.com";
        var company = $"NewCo-{Guid.NewGuid():N}";
        factory.Email.Sent.Clear();
        factory.HarvestQueue.Jobs.Clear();

        // Act
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite", new()
        {
            ["Input.Company"] = company,
            ["Input.Email"] = invitee,
            ["Input.Niche"] = "dental clinics in the Benelux",
            ["Input.Info"] = "we sell scheduling software",
        });

        // Assert — invite email sent, harvest enqueued, profile persisted with generated search fields
        factory.Email.Sent.Should().ContainSingle(m => m.To == invitee);

        Guid customerId;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            customerId = (await users.FindByEmailAsync(invitee))!.CustomerId!.Value;
            var customers = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
            var customer = await customers.GetByNameAsync(company, CancellationToken.None);
            customer!.Profile.Niche.Should().Be("dental clinics in the Benelux");
            customer.Profile.TargetKeywords.Should().Be("dentist\nclinic");   // from FakeSearchProfileGenerator
            customer.HarvestStatus.Should().Be(HarvestStatus.Pending);
        }
        factory.HarvestQueue.Jobs.Should().ContainSingle(j => j.CustomerId == customerId);
    }

    [Fact]
    public async Task Invite_WithoutNiche_IsRejected()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
        var invitee = $"noniche-{Guid.NewGuid():N}@test.com";
        factory.Email.Sent.Clear();

        // Act — omit Input.Niche (required)
        var html = await TestAuth.PostFormReadAsync(client, "/admin?handler=Invite", new()
        {
            ["Input.Company"] = "X Co", ["Input.Email"] = invitee,
        });

        // Assert — no invite sent; the form redisplays
        factory.Email.Sent.Should().NotContain(m => m.To == invitee);
    }

    [Fact]
    public async Task InviteFromInquiry_PrefillsEmailNicheAndInfo()
    {
        // Arrange — persist an inquiry, then open the invite page for it
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
        Guid inquiryId;
        var inqEmail = $"lead-{Guid.NewGuid():N}@prospect.com";
        using (var scope = factory.Services.CreateScope())
        {
            var inquiries = scope.ServiceProvider.GetRequiredService<IInquiryRepository>();
            var inq = Inquiry.Create("Jane Prospect", new EmailAddress(inqEmail), "yoga studios", "class-booking app", "please help", DateTimeOffset.UtcNow);
            await inquiries.AddAsync(inq, CancellationToken.None);
            inquiryId = inq.Id.Value;
        }

        // Act
        var html = await client.GetStringAsync($"/admin?useInquiry={inquiryId}");

        // Assert — the invite form is prefilled from the inquiry
        html.Should().Contain(inqEmail);
        html.Should().Contain("yoga studios");
        html.Should().Contain("class-booking app");
    }
}
```

- [ ] **Step 2: Run — expect FAIL.** Run: `dotnet test tests/Coldstart.Web.Tests --filter OnboardingInviteTests`

- [ ] **Step 3: Update the page model `Index.cshtml.cs`** — extend the constructor, `InputModel`, add the inquiry read model + list, rewrite `OnGetAsync`, and extend `OnPostInviteAsync`.

Constructor — add the three deps:
```csharp
public sealed class IndexModel(
    UserManager<ApplicationUser> users,
    IProvisionCustomer provision,
    ICustomerRepository customers,
    IDeleteCustomer deleteCustomer,
    IEmailSender email,
    IInquiryRepository inquiries,
    ISearchProfileGenerator profileGen,
    Coldstart.Application.Harvest.IHarvestQueue harvestQueue) : PageModel
```
Add `using Coldstart.Application.Customers;` (already present) and `using Coldstart.Application.Harvest;` (for `HarvestJob`), plus `Coldstart.Domain` for `InquiryId`.

Extend `InputModel`:
```csharp
    public sealed class InputModel
    {
        [Required] public string Company { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required] public string Niche { get; set; } = "";
        public string? Info { get; set; }
    }
```

Add an inquiry read model + list property (near `Rows`):
```csharp
    public IReadOnlyList<InquiryRow> Inquiries { get; private set; } = [];

    public sealed record InquiryRow(Guid Id, string Name, string Email, string Niche, string WhatYouSell, string? Message, DateTimeOffset CreatedAt);
```

Rewrite `OnGetAsync` to accept an optional inquiry and prefill:
```csharp
    public async Task OnGetAsync(Guid? useInquiry, CancellationToken ct)
    {
        if (useInquiry is { } inqId)
        {
            var inq = await inquiries.GetAsync(new InquiryId(inqId), ct);
            if (inq is not null)
                Input = new InputModel
                {
                    Email = inq.Email.Value,
                    Niche = inq.Niche,
                    Info = string.IsNullOrWhiteSpace(inq.Message) ? inq.WhatYouSell : $"{inq.WhatYouSell}\n\n{inq.Message}",
                };
        }
        await LoadAsync(ct);
    }
```
(If `InquiryId` has no public `(Guid)` constructor, use the factory the VO exposes — mirror `CustomerId`/`LeadId` usage elsewhere.)

In `OnPostInviteAsync`, replace the provisioning line and add generate + enqueue. The block currently reads:
```csharp
        await users.AddToRoleAsync(user, ColdstartRoles.CompanyAdmin);
        user.CustomerId = await provision.ProvisionAsync(Input.Company, ct);
        await users.UpdateAsync(user);

        await SendInviteAsync(user, Input.Company, ct);
```
Replace with:
```csharp
        await users.AddToRoleAsync(user, ColdstartRoles.CompanyAdmin);

        SearchProfile? profile = null;
        try { profile = await profileGen.GenerateAsync(new SearchProfileRequest(Input.Niche, Input.Info ?? ""), ct); }
        catch { /* generation must never block the invite */ }

        user.CustomerId = await provision.ProvisionAsync(
            Input.Company, new CompanyProfileSeed(Input.Niche, Input.Info, profile), ct);
        await users.UpdateAsync(user);

        await SendInviteAsync(user, Input.Company, ct);
        harvestQueue.Enqueue(new HarvestJob(user.CustomerId.Value));
```

Extend `LoadAsync` to populate `Inquiries` (add before the method returns):
```csharp
        Inquiries = (await inquiries.RecentAsync(20, ct))
            .Select(i => new InquiryRow(i.Id.Value, i.Name, i.Email.Value, i.Niche, i.WhatYouSell, i.Message, i.CreatedAt))
            .ToList();
```

- [ ] **Step 4: Update the page `Index.cshtml`** — add Niche + Info fields to the invite form, and an inquiries list that links to prefill. Replace the invite `<form>` with:
```html
  <form method="post" asp-page-handler="Invite" class="admin-invite">
    <div asp-validation-summary="All" class="field-error"></div>
    <div class="field"><label asp-for="Input.Company">Company name</label><input asp-for="Input.Company" required></div>
    <div class="field"><label asp-for="Input.Email">Admin email</label><input asp-for="Input.Email" type="email" required></div>
    <div class="field"><label asp-for="Input.Niche">Who their customers are (niche)</label><input asp-for="Input.Niche" required placeholder="e.g. dental clinics in the Benelux"></div>
    <div class="field"><label asp-for="Input.Info">What they sell / notes</label><textarea asp-for="Input.Info" rows="3" placeholder="from their inquiry, or a short description"></textarea></div>
    <button class="btn btn-primary" type="submit">Send invite →</button>
  </form>
```
Add an inquiries section above "Companies & users":
```html
  @if (Model.Inquiries.Count > 0)
  {
    <h2 class="app-group-head">Recent inquiries</h2>
    <table class="admin-table">
      <thead><tr><th>Name</th><th>Email</th><th>Niche</th><th>Sells</th><th></th></tr></thead>
      <tbody>
      @foreach (var q in Model.Inquiries)
      {
        <tr>
          <td>@q.Name</td>
          <td>@q.Email</td>
          <td>@q.Niche</td>
          <td>@q.WhatYouSell</td>
          <td class="admin-actions"><a class="btn btn-ghost btn-sm" href="/admin?useInquiry=@q.Id">Use for invite</a></td>
        </tr>
      }
      </tbody>
    </table>
  }
```

- [ ] **Step 5: Run — expect PASS.** Run: `dotnet test tests/Coldstart.Web.Tests --filter OnboardingInviteTests`

- [ ] **Step 6: Run the full Web suite** (the new required `Niche` field means any OTHER test that posts `/admin?handler=Invite` with only Company+Email now fails ModelState). Fix such calls in `AdminConsoleTests.cs` / `AdminInviteTests.cs` by adding `["Input.Niche"] = "test niche"` to their invite `PostFormAsync` field dictionaries. Run: `dotnet test tests/Coldstart.Web.Tests`

- [ ] **Step 7: Commit**
```bash
git add -A && git commit -m "feat(admin): invite captures niche/info, generates profile, provisions + enqueues harvest"
```

---

## Task 11: Inbox foil + onboarding state + upgrade CTA

The new user's briefing: unlocked leads render normally; locked leads are blurred cards (no link, no actions, not draggable) with a true remaining count and an "Unlock all leads" CTA; an onboarding message shows while the first harvest runs.

**Files:**
- Modify: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml.cs`, `src/Coldstart.Web/Areas/App/Pages/Index.cshtml`
- Create: `tests/Coldstart.Web.Tests/FoilWebFactory.cs`, `tests/Coldstart.Web.Tests/LeadFoilTests.cs`

**Interfaces consumed:** `LeadInbox.Plan`/`LockedCount`/`HarvestStatus`, `LeadSummary.Locked`, `CustomerPlan`, `HarvestStatus`.

- [ ] **Step 1: Add `FoilWebFactory.cs`** — same as the shared factory but with a small quota so locking triggers:
```csharp
using Microsoft.AspNetCore.Hosting;

namespace Coldstart.Web.Tests;

// A factory with a SMALL free quota so the foil actually engages (the shared factory sets it huge).
public sealed class FoilWebFactory : ColdstartWebFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Quota:FreeLeadCount", "2");   // overrides the base's huge value (last wins)
    }
}
```
(`ColdstartWebFactory.ConfigureWebHost` must be `protected override void` already — it is. No change needed there.)

- [ ] **Step 2: Write failing `LeadFoilTests.cs`**
```csharp
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using Coldstart.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class LeadFoilTests(FoilWebFactory factory) : IClassFixture<FoilWebFactory>
{
    private async Task AddLeadsAsync(string email, int count)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var cid = new CustomerId((await users.FindByEmailAsync(email))!.CustomerId!.Value);
        for (var i = 0; i < count; i++)
            db.Leads.Add(Lead.CreateHarvested(cid, $"Co{i}", Fit.Yes, $"subj{i}", "body",
                "hackernews", "https://x/1", $"fp-{Guid.NewGuid():N}", 0.9 - i * 0.01, "outbound", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Inbox_ForFreeCustomer_ShowsUnlockCtaWithRemainingCount()
    {
        // Arrange — 5 leads, quota 2 → 3 locked
        var client = factory.CreateClient();
        var email = $"foil-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        await AddLeadsAsync(email, 5);

        // Act
        var html = await client.GetStringAsync("/app?tab=new");

        // Assert — the foil is shown with the true remaining count and the CTA
        html.Should().Contain("Unlock all leads");
        html.Should().Contain("3 more");
        html.Should().Contain("is-locked");   // at least one blurred card
    }

    [Fact]
    public async Task LockedLeadDetail_RedirectsToApp()
    {
        // Arrange — find a locked lead's id (lowest-score of the 5)
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"foil-detail-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        await AddLeadsAsync(email, 5);
        Guid lockedId;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
            var cid = (await users.FindByEmailAsync(email))!.CustomerId!.Value;
            // lowest score = last after the ranking = locked
            lockedId = db.Leads.IgnoreQueryFilters().Where(l => l.CustomerId == new CustomerId(cid))
                .OrderBy(l => l.Score).First().Id.Value;
        }

        // Act
        var response = await client.GetAsync($"/app/leads/{lockedId}");

        // Assert — the locked lead is not viewable
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/app");
    }

    [Fact]
    public async Task Inbox_WhileHarvestPending_AndNoLeads_ShowsFindingLeadsMessage()
    {
        // Arrange — a customer whose harvest is Pending with zero leads
        var client = factory.CreateClient();
        var email = $"pending-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
            var cid = (await users.FindByEmailAsync(email))!.CustomerId!.Value;
            var customer = db.Customers.IgnoreQueryFilters().First(c => c.Id == new CustomerId(cid));
            customer.MarkHarvest(HarvestStatus.Pending, null);
            await db.SaveChangesAsync();
        }

        // Act
        var html = await client.GetStringAsync("/app?tab=new");

        // Assert
        html.Should().Contain("Finding your first leads");
    }
}
```

- [ ] **Step 3: Run — expect FAIL.** Run: `dotnet test tests/Coldstart.Web.Tests --filter LeadFoilTests`

- [ ] **Step 4: Expose foil state on the App `IndexModel.cs`** — add properties and set them in `OnGetAsync`. Add near the other bucket properties:
```csharp
    public Coldstart.Domain.CustomerPlan Plan { get; private set; }
    public Coldstart.Domain.HarvestStatus HarvestStatus { get; private set; }
    public int LockedCount { get; private set; }
    public int TotalLeads { get; private set; }
```
In `OnGetAsync`, right after `CustomerName = result.CustomerName;`, add:
```csharp
        Plan = result.Plan;
        HarvestStatus = result.HarvestStatus;
        LockedCount = result.LockedCount;
        TotalLeads = result.Leads.Count;
```

- [ ] **Step 5: Update the App `Index.cshtml`** — foil styles, onboarding briefing, locked cards, and the upgrade CTA + dialog.

At the very top (after the `@{ ... }` block), add a page-scoped style block:
```html
<style>
  .lead-row.is-locked { position: relative; }
  .lead-row.is-locked .lead-row-main { filter: blur(4px); pointer-events: none; user-select: none; }
  .app-foil { margin: 16px 0; padding: 16px 20px; border: 1px dashed #C7BFA9; border-radius: 12px; background: #FBF8F1; text-align: center; }
  .app-foil p { margin: 0 0 10px; color: #5D5A50; }
</style>
```

Rewrite the briefing `<section class="app-briefing">` to handle the onboarding states first:
```html
  <section class="app-briefing">
    @if (Model.TotalLeads == 0 && Model.HarvestStatus is Coldstart.Domain.HarvestStatus.Pending or Coldstart.Domain.HarvestStatus.Running)
    {
      <h1>Finding your first leads…</h1>
      <p class="app-briefing-ts">This takes a few minutes. Refresh shortly and they'll appear here.</p>
    }
    else if (Model.TotalLeads == 0 && Model.HarvestStatus == Coldstart.Domain.HarvestStatus.Failed)
    {
      <h1>We couldn't fetch leads yet</h1>
      <p class="app-briefing-ts">We'll try again — check back soon.</p>
    }
    else if (overnight > 0)
    {
      <h1>Good morning — @overnight new @(overnight == 1 ? "lead" : "leads") found overnight</h1>
      @if (latest is not null) { <p class="app-briefing-ts ts">latest found @latest.Value.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) UTC</p> }
    }
    else
    {
      <h1>Good morning — no new leads this morning</h1>
      <p class="app-briefing-ts">The engine runs again tonight.</p>
    }
  </section>
```

Update the `Card` local function to blur locked leads — replace the `void Card(...)` body:
```csharp
    void Card(Coldstart.Application.Leads.LeadSummary lead, bool draggable, bool actionable)
    {
      var locked = lead.Locked;
      <article class="lead-row @(locked ? "is-locked" : "")" data-lead-id="@lead.Id" draggable="@(draggable && !locked ? "true" : "false")">
        @if (locked)
        {
          <div class="lead-row-main">
            <div class="lead-row-head">
              <span class="lead-row-co">@lead.Company</span>
              <span class="fit fit-@lead.Fit.ToLower()">@lead.Fit</span>
            </div>
            <div class="lead-row-sub"><span class="lead-row-subj">@lead.Subject</span></div>
          </div>
        }
        else
        {
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
        }
      </article>
    }
```

Add the foil banner + CTA immediately after the `<nav class="app-tabs">…</nav>` block (visible whenever the customer has locked leads):
```html
  @if (Model.LockedCount > 0)
  {
    <div class="app-foil">
      <p><strong>@Model.LockedCount more @(Model.LockedCount == 1 ? "lead" : "leads")</strong> ready behind your free plan.</p>
      <button class="btn btn-primary" type="button" onclick="document.getElementById('upgrade').showModal()">Unlock all leads</button>
    </div>
  }
```

Add the upgrade dialog just before the closing `</main>` (next to the existing `move-error` dialog):
```html
  <dialog id="upgrade" class="lead-modal" aria-labelledby="upgrade-title">
    <form method="dialog" class="lead-modal-card">
      <p class="lead-modal-eyebrow">Free plan</p>
      <h2 class="lead-modal-title" id="upgrade-title">Unlock every lead</h2>
      <p class="lead-modal-detail">Your free plan shows a sample. To see and send every lead we find, upgrade your plan — email <a href="mailto:andrzej@getcoldstart.nl">andrzej@getcoldstart.nl</a> and we'll set it up. (Self-serve subscriptions are coming soon.)</p>
      <div class="lead-modal-actions"><button class="btn btn-primary" value="ok" autofocus>Got it</button></div>
    </form>
  </dialog>
```

- [ ] **Step 6: Run — expect PASS.** Run: `dotnet test tests/Coldstart.Web.Tests --filter LeadFoilTests`

- [ ] **Step 7: Run the whole suite** to confirm nothing regressed. Run: `dotnet test`
Expected: all green (Domain, Application, Infrastructure, Web).

- [ ] **Step 8: Commit**
```bash
git add -A && git commit -m "feat(app): free-tier foil (blurred locked leads + unlock CTA) + onboarding harvest state"
```

---

## Notes for the executor

- **Migration timing:** only Task 1 adds a migration. Every later task is code-only.
- **Keeping the suite green:** Task 2 updates the whole read path (records + `ListLeads` + `GetLeadInbox`) together, so the Application project always compiles. Task 3 changes the `LeadWorkflow` ctor — update ALL existing `new LeadWorkflow(...)` in `LeadWorkflowTests` (and `LeadWorkflowSendTests` if it constructs one) in the same task. Task 10 makes `Input.Niche` required — fix every other test that posts an invite with only Company+Email (Task 10 Step 6).
- **Background worker in tests:** the real `HarvestQueueService` runs in Web tests but idles because `IHarvestQueue` is faked; never let a test rely on the real worker.
- **`InquiryId` construction:** the plan uses `new InquiryId(guid)` (mirrors `CustomerId`/`LeadId`). If the VO exposes a different ctor/factory, use that.

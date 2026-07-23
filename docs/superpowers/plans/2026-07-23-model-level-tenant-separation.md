# Model-level Tenant Separation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make tenant isolation the persistence-layer default via EF Core global query filters driven by an ambient tenant context, and make lead dedup per-tenant. Per `docs/superpowers/specs/2026-07-23-model-level-tenant-separation-design.md`.

**Architecture:** A tenant-context abstraction (`ITenantContext` read / `ITenantScope` write) resolved from a `customer_id` claim in Web and set explicitly in the Harvester. `ColdstartDbContext` applies `HasQueryFilter` on `Lead` and `Customer`. Cross-tenant system paths opt out with `IgnoreQueryFilters()`. Dedup fingerprint uniqueness moves from global to composite `(CustomerId, Fingerprint)`.

**Tech Stack:** .NET 10, EF Core + Npgsql, ASP.NET Core Identity, xUnit + FluentAssertions + NSubstitute, Testcontainers Postgres (`PostgresFixture`) + WebApplicationFactory (`ColdstartWebFactory`).

## Global Constraints

- Hexagonal: `ITenantContext`/`ITenantScope` are **Application** ports; `MutableTenantContext` and `HttpTenantContext` are **Infrastructure**. Application never references `HttpContext`/EF types.
- **Fail closed:** when no tenant is set, filtered reads return nothing. The default `ITenantContext` yields `null`.
- The **only** intentional isolation lifts are explicit `IgnoreQueryFilters()` in `CustomerRepository.GetByNameAsync` and `AllAsync`. No global "bypass mode".
- Sequencing rule: the tenant must be **resolved in every host (Web claim, Harvester scope) before query filters are switched on** (Task 4), so the test suite stays green at every commit.
- `CustomerId`/`LeadId` are `sealed record`s with EF value conversions. Query filters compare the whole value object (`l.CustomerId == CurrentCustomerId` where `CurrentCustomerId` is `CustomerId?`); verify translation in the first isolation test and, if EF cannot translate it, fall back to a mapped raw-Guid shadow comparison (noted inline).
- App-level ownership checks (`LeadWorkflow.MutateAsync`) stay untouched (defense-in-depth).
- Async + `CancellationToken`; `internal` impls; conventional commits, **no `Co-Authored-By`/"Generated with" trailers**; stage only listed paths.
- Full-solution `dotnet test` green before each commit (report per-project totals). Infrastructure integration tests need Docker (Testcontainers).

---

### Task 1: Tenant-context abstraction + DbContext plumbing (no filters yet)

**Files:**
- Create: `src/Coldstart.Application/Ports/ITenantContext.cs`
- Create: `src/Coldstart.Infrastructure/Tenancy/MutableTenantContext.cs`
- Modify: `src/Coldstart.Infrastructure/ColdstartDbContext.cs` (inject context, expose `CurrentCustomerId`)
- Modify: `src/Coldstart.Infrastructure/DependencyInjection.cs` (register)
- Modify: `tests/Coldstart.Infrastructure.Tests/PostgresFixture.cs` (pass a tenant context)
- Test: `tests/Coldstart.Infrastructure.Tests/MutableTenantContextTests.cs`

**Interfaces:**
- Produces: `ITenantContext { Guid? CustomerId }`, `ITenantScope { void SetCurrent(Guid customerId) }`, `MutableTenantContext` (implements both), `ColdstartDbContext.CurrentCustomerId : CustomerId?`, `PostgresFixture.NewDbContext(Guid? tenantId = null)`.

- [ ] **Step 1: Write the ports**

Create `src/Coldstart.Application/Ports/ITenantContext.cs`:

```csharp
namespace Coldstart.Application.Ports;

// Ambient current tenant. Null => no tenant established (anonymous/system/background before scoping);
// filtered reads then return nothing (fail closed).
public interface ITenantContext
{
    Guid? CustomerId { get; }
}

// Write side: background/host code sets the current tenant for the remainder of the scope.
public interface ITenantScope
{
    void SetCurrent(Guid customerId);
}
```

- [ ] **Step 2: Write the mutable impl + failing test**

Create `src/Coldstart.Infrastructure/Tenancy/MutableTenantContext.cs`:

```csharp
using Coldstart.Application.Ports;

namespace Coldstart.Infrastructure.Tenancy;

// Scoped tenant holder for background hosts (harvester) and tests. Default: no tenant (fail closed).
internal sealed class MutableTenantContext : ITenantContext, ITenantScope
{
    public Guid? CustomerId { get; private set; }
    public void SetCurrent(Guid customerId) => CustomerId = customerId;
}
```

Create `tests/Coldstart.Infrastructure.Tests/MutableTenantContextTests.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Infrastructure.Tenancy;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class MutableTenantContextTests
{
    [Fact]
    public void Default_HasNoTenant()
    {
        // Arrange / Act
        var ctx = new MutableTenantContext();

        // Assert
        ctx.CustomerId.Should().BeNull();
    }

    [Fact]
    public void SetCurrent_MakesTenantVisibleThroughReadInterface()
    {
        // Arrange
        var ctx = new MutableTenantContext();
        var id = Guid.NewGuid();

        // Act
        ((ITenantScope)ctx).SetCurrent(id);

        // Assert
        ((ITenantContext)ctx).CustomerId.Should().Be(id);
    }
}
```

`MutableTenantContext` is `internal`; the test project already sees internals via `[InternalsVisibleTo]` (used by other infra tests). If not, add it to `Coldstart.Infrastructure`'s csproj — verify first.

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~MutableTenantContext"`
Expected: compile failure — types don't exist yet.

- [ ] **Step 4: Thread the context into the DbContext**

Rewrite `src/Coldstart.Infrastructure/ColdstartDbContext.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Infrastructure;

public sealed class ColdstartDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ITenantContext _tenant;

    public ColdstartDbContext(DbContextOptions<ColdstartDbContext> options, ITenantContext tenant)
        : base(options) => _tenant = tenant;

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Inquiry> Inquiries => Set<Inquiry>();

    // Current tenant as the domain key type, for query filters. Null => no rows match (fail closed).
    public CustomerId? CurrentCustomerId => _tenant.CustomerId is { } id ? new CustomerId(id) : null;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // Identity schema
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ColdstartDbContext).Assembly);
        // Query filters are added in Task 4 (after every host resolves the tenant).
    }
}
```

- [ ] **Step 5: Register the context services + default tenant**

In `src/Coldstart.Infrastructure/DependencyInjection.cs`, add (top of `AddInfrastructure`, before/after `AddDbContext`; needs `using Microsoft.Extensions.DependencyInjection.Extensions;` — already imported):

```csharp
        services.AddScoped<Tenancy.MutableTenantContext>();
        services.TryAddScoped<Application.Ports.ITenantContext>(sp => sp.GetRequiredService<Tenancy.MutableTenantContext>());
        services.TryAddScoped<Application.Ports.ITenantScope>(sp => sp.GetRequiredService<Tenancy.MutableTenantContext>());
```

`AddDbContext<ColdstartDbContext>` injects the registered `ITenantContext` into the new 2-arg constructor automatically.

- [ ] **Step 6: Update the Postgres fixture to supply a tenant**

In `tests/Coldstart.Infrastructure.Tests/PostgresFixture.cs`, replace `NewDbContext()`:

```csharp
    // A settable tenant per context; default null. Existing tests that pass no id keep working
    // until Task 4 adds filters, at which point tenant-scoped reads pass the owning customer's id.
    public ColdstartDbContext NewDbContext(Guid? tenantId = null)
    {
        var options = new DbContextOptionsBuilder<ColdstartDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        var tenant = new MutableTenantContext();
        if (tenantId is { } id) ((Coldstart.Application.Ports.ITenantScope)tenant).SetCurrent(id);
        return new ColdstartDbContext(options, tenant);
    }
```

Add `using Coldstart.Infrastructure.Tenancy;` to the fixture. `InitializeAsync`'s `NewDbContext()` call still works (tenant null; no filters yet).

- [ ] **Step 7: Run tests**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~MutableTenantContext"` → PASS.
Then full `dotnet test` → green (constructor change compiles; `ColdstartWebFactory`/Program resolve `ITenantContext` from the new default registration; no behavior change).

- [ ] **Step 8: Commit**

```bash
git add src/Coldstart.Application/Ports/ITenantContext.cs src/Coldstart.Infrastructure/Tenancy/MutableTenantContext.cs src/Coldstart.Infrastructure/ColdstartDbContext.cs src/Coldstart.Infrastructure/DependencyInjection.cs tests/Coldstart.Infrastructure.Tests/PostgresFixture.cs tests/Coldstart.Infrastructure.Tests/MutableTenantContextTests.cs
git commit -m "feat(tenancy): ambient tenant context wired into the DbContext (no filters yet)"
```

---

### Task 2: Web tenant resolution from a sign-in claim (no filters yet)

**Files:**
- Create: `src/Coldstart.Infrastructure/Identity/AppUserClaimsPrincipalFactory.cs`
- Create: `src/Coldstart.Infrastructure/Tenancy/HttpTenantContext.cs`
- Modify: `src/Coldstart.Web/Program.cs` (HttpContextAccessor, claims factory, override `ITenantContext`)
- Test: `tests/Coldstart.Web.Tests/TenantClaimTests.cs`

**Interfaces:**
- Consumes: `ITenantContext` (Task 1), `ApplicationUser.CustomerId`.
- Produces: `customer_id` claim on the signed-in principal; `HttpTenantContext` resolving it.

- [ ] **Step 1: Write the claims factory + Http context**

Create `src/Coldstart.Infrastructure/Identity/AppUserClaimsPrincipalFactory.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Identity;

// Stamps the tenant onto the auth cookie at sign-in so tenant resolution is a claim read (no DB hit).
public sealed class AppUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, options)
{
    public const string CustomerIdClaim = "customer_id";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.CustomerId is { } cid)
            identity.AddClaim(new Claim(CustomerIdClaim, cid.ToString()));
        return identity;
    }
}
```

Create `src/Coldstart.Infrastructure/Tenancy/HttpTenantContext.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;

namespace Coldstart.Infrastructure.Tenancy;

// Reads the tenant from the signed-in principal's customer_id claim. Null when anonymous/absent.
internal sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid? CustomerId
    {
        get
        {
            var value = accessor.HttpContext?.User?.FindFirst(AppUserClaimsPrincipalFactory.CustomerIdClaim)?.Value;
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }
}
```

- [ ] **Step 2: Add an Infrastructure registration helper (keeps `HttpTenantContext` internal)**

`HttpTenantContext` is `internal`, so Web's composition root cannot name it. Add a helper in `src/Coldstart.Infrastructure/DependencyInjection.cs`:

```csharp
    // Web-only: resolve the tenant from the signed-in principal's claim (overrides the default context).
    public static IServiceCollection AddHttpTenantResolution(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<Application.Ports.ITenantContext, Tenancy.HttpTenantContext>();
        return services;
    }
```

- [ ] **Step 3: Wire Web to use them**

In `src/Coldstart.Web/Program.cs`:

Add the claims factory to the Identity builder (chain onto the existing `AddIdentity` at line 22-24; `AppUserClaimsPrincipalFactory` is `public`):

```csharp
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>();
```

After `AddInfrastructure(...)` (after line 15), override the tenant context with the HTTP one:

```csharp
builder.Services.AddHttpTenantResolution();
```

(`AddScoped` inside the helper runs after the `TryAddScoped` default, so `HttpTenantContext` wins for `ITenantContext`; `ITenantScope`/`MutableTenantContext` remain registered but unused in Web.) Add `using Coldstart.Infrastructure;` if not already present.

- [ ] **Step 4: Write the failing test**

Create `tests/Coldstart.Web.Tests/TenantClaimTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class TenantClaimTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    // A signed-in user's requests must carry a resolvable tenant. We prove it end-to-end: the
    // signed-in user can load their own briefing (200), which only works if the tenant resolves.
    // (Direct isolation is proven at the persistence layer in Task 4.)
    [Fact]
    public async Task SignedInUser_HasResolvableTenant_AndReachesTheirBriefing()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"tenant-claim-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");

        // Act
        var res = await client.GetAsync("/app");

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 5: Run**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~TenantClaim"` → should PASS once wired (compile first to confirm the factory chain is valid).
Then full `dotnet test` → green. (No filters yet, so all existing Web tests behave unchanged; the claim is merely present.)

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Infrastructure/Identity/AppUserClaimsPrincipalFactory.cs src/Coldstart.Infrastructure/Tenancy/HttpTenantContext.cs src/Coldstart.Web/Program.cs tests/Coldstart.Web.Tests/TenantClaimTests.cs
git commit -m "feat(tenancy): resolve the web tenant from a customer_id sign-in claim"
```

---

### Task 3: Harvester sets the tenant scope before dedup (no filters yet)

**Files:**
- Modify: `src/Coldstart.Application/Harvest/HarvestLeads.cs` (inject `ITenantScope`, resolve target first, set scope)
- Test: `tests/Coldstart.Infrastructure.Tests/HarvestEndToEndTests.cs` (assert leads land under the target tenant — likely already covered; add if not)

**Interfaces:**
- Consumes: `ITenantScope` (Task 1), existing `ICustomerRepository.GetByNameAsync`, `NicheOptions.TargetCustomerName`.

- [ ] **Step 1: Reorder + set the scope**

In `src/Coldstart.Application/Harvest/HarvestLeads.cs`, add `ITenantScope tenantScope` to the primary constructor parameter list (add `using Coldstart.Application.Ports;`). At the very start of `RunAsync`, before Phase 1, resolve the target customer and set the scope so Phase-1 dedup (`ExistsByFingerprintAsync`) is tenant-scoped once filters exist:

```csharp
    public async Task<HarvestReport> RunAsync(CancellationToken ct)
    {
        var target = await ResolveTargetCustomerAsync(ct);   // moved up; uses GetByNameAsync (unfiltered in Task 4)
        tenantScope.SetCurrent(target.Id.Value);

        int harvested = 0, scoredIn = 0, deduped = 0, enriched = 0, saved = 0, rejected = 0;
        // ... existing Phase 1 unchanged ...
```

Then in Phase 2, delete the now-duplicate `var target = await ResolveTargetCustomerAsync(ct);` line and keep using the `target`/`sender` already resolved (move the `sender` line up next to `target`, or keep it in Phase 2 using the hoisted `target`). Ensure `target` and `sender` are in scope for Phase 2. Do not change dedup/enrich logic otherwise.

> If `ResolveTargetCustomerAsync` provisions/creates a customer when missing, that write is unaffected by read filters.

- [ ] **Step 2: Confirm/add the end-to-end assertion**

In `tests/Coldstart.Infrastructure.Tests/HarvestEndToEndTests.cs`, ensure a test asserts harvested leads are persisted under the resolved target customer's id. If an equivalent assertion already exists, no new test is needed — state that in the task report. Otherwise add one following the file's existing fixture pattern. Constructing `HarvestLeads` directly in a test now requires passing an `ITenantScope` (use a `new MutableTenantContext()` and read the resulting tenant back through it).

- [ ] **Step 3: Run**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~HarvestEndToEnd"` → PASS.
Then full `dotnet test` → green (ordering change is behavior-preserving while filters are off).

- [ ] **Step 4: Commit**

```bash
git add src/Coldstart.Application/Harvest/HarvestLeads.cs tests/Coldstart.Infrastructure.Tests/HarvestEndToEndTests.cs
git commit -m "refactor(harvest): resolve target tenant first and set the tenant scope before dedup"
```

---

### Task 4: Switch on query filters for Lead + Customer + system bypasses

**Files:**
- Modify: `src/Coldstart.Infrastructure/ColdstartDbContext.cs` (add `HasQueryFilter`)
- Modify: `src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs` (`IgnoreQueryFilters` on `GetByNameAsync`, `AllAsync`, and `DeleteAsync`'s reads if an admin test needs it)
- Modify: `src/Coldstart.Infrastructure/DbSeeder.cs` (`IgnoreQueryFilters` on the seed guard)
- Modify: existing Infra tests that read tenant-scoped data via `PostgresFixture` (set the tenant)
- Test: `tests/Coldstart.Infrastructure.Tests/TenantIsolationTests.cs` (new)

**Interfaces:**
- Consumes: `ColdstartDbContext.CurrentCustomerId` (Task 1), tenant resolution in Web (Task 2) + Harvester (Task 3).

- [ ] **Step 1: Add the filters**

In `ColdstartDbContext.OnModelCreating`, after `ApplyConfigurationsFromAssembly`, add:

```csharp
        // Fail-closed tenant isolation. Owned LeadNotes load with their Lead, so they inherit this.
        modelBuilder.Entity<Lead>().HasQueryFilter(l => l.CustomerId == CurrentCustomerId);
        modelBuilder.Entity<Customer>().HasQueryFilter(c => c.Id == CurrentCustomerId);
```

> Value-object comparison: `CustomerId` is a value-converted `record`. If `dotnet test` surfaces an EF translation error for these filters, replace the comparison with a raw-Guid form: add `private Guid? CurrentCustomerIdValue => _tenant.CustomerId;` and filter on a mapped Guid (e.g. compare `EF.Property<Guid>(l, "CustomerId") == CurrentCustomerIdValue` or expose the underlying Guid). Prefer the value-object form; only fall back if translation fails, and note which was used in the report.

- [ ] **Step 2: Add the system bypasses**

In `src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs`, add `.IgnoreQueryFilters()` to the two system-path queries:

```csharp
    public async Task<IReadOnlyList<Customer>> AllAsync(CancellationToken ct) =>
        await db.Customers.IgnoreQueryFilters().OrderBy(c => c.Name).ToListAsync(ct);

    public async Task<Customer?> GetByNameAsync(string name, CancellationToken ct) =>
        await db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Name == name, ct);
```

Leave `GetAsync(CustomerId id)` filtered (a tenant loads only itself; `DeleteAsync` is admin — see Step 4 note).

- [ ] **Step 3: Write the isolation tests (new)**

Create `tests/Coldstart.Infrastructure.Tests/TenantIsolationTests.cs`:

```csharp
using Coldstart.Domain;
using Coldstart.Infrastructure.Repositories;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class TenantIsolationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Lead_IsNotVisible_ToAnotherTenant()
    {
        // Arrange — two customers, one lead each (distinct fingerprints: composite index lands in Task 5)
        Guid customerA, customerB, leadA;
        await using (var seed = fixture.NewDbContext())
        {
            var a = Customer.Create($"A-{Guid.NewGuid():N}");
            var b = Customer.Create($"B-{Guid.NewGuid():N}");
            var la = a.AddLead("Acme", Fit.Yes, "s", "b", DateTimeOffset.UtcNow);
            var lb = b.AddLead("Globex", Fit.Yes, "s", "b", DateTimeOffset.UtcNow);
            seed.Customers.AddRange(a, b);
            await seed.SaveChangesAsync();
            customerA = a.Id.Value; customerB = b.Id.Value; leadA = la.Id.Value;
        }

        // Act — read as tenant B
        await using var asB = fixture.NewDbContext(customerB);
        var repoB = new LeadRepository(asB);
        var crossRead = await repoB.GetAsync(new LeadId(leadA), CancellationToken.None);
        var ownRead = await repoB.ForCustomerAsync(new CustomerId(customerA), CancellationToken.None);

        // Assert — B cannot see A's lead by id, and a scoped list for A returns nothing under B's context
        crossRead.Should().BeNull();
        ownRead.Should().BeEmpty();

        // And A sees its own lead
        await using var asA = fixture.NewDbContext(customerA);
        (await new LeadRepository(asA).GetAsync(new LeadId(leadA), CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task Customer_IsNotVisible_ToAnotherTenant_ButAdminBypassSeesAll()
    {
        // Arrange
        Guid a, b;
        await using (var seed = fixture.NewDbContext())
        {
            var ca = Customer.Create($"A-{Guid.NewGuid():N}");
            var cb = Customer.Create($"B-{Guid.NewGuid():N}");
            seed.Customers.AddRange(ca, cb);
            await seed.SaveChangesAsync();
            a = ca.Id.Value; b = cb.Id.Value;
        }

        // Act / Assert — as tenant A, GetAsync(B) is filtered out; admin AllAsync (IgnoreQueryFilters) sees both
        await using var asA = fixture.NewDbContext(a);
        var repoA = new CustomerRepository(asA);
        (await repoA.GetAsync(new CustomerId(b), CancellationToken.None)).Should().BeNull();
        (await repoA.GetAsync(new CustomerId(a), CancellationToken.None)).Should().NotBeNull();
        var all = await repoA.AllAsync(CancellationToken.None);
        all.Select(c => c.Id.Value).Should().Contain(new[] { a, b });
    }

    [Fact]
    public async Task GetByName_FindsCustomer_WithNoTenantSet()
    {
        // Arrange — provisioning path: no tenant established yet
        var name = $"Prov-{Guid.NewGuid():N}";
        await using (var seed = fixture.NewDbContext())
        {
            seed.Customers.Add(Customer.Create(name));
            await seed.SaveChangesAsync();
        }

        // Act
        await using var noTenant = fixture.NewDbContext();
        var found = await new CustomerRepository(noTenant).GetByNameAsync(name, CancellationToken.None);

        // Assert
        found.Should().NotBeNull();
    }
}
```

- [ ] **Step 4: Fix existing Infra tests that now read filtered data**

Run the Infra suite and update every test that reads tenant-scoped data through `PostgresFixture` to pass the owning customer's id to `NewDbContext(id)`. Known files to review: `LeadRepositoryTests` (e.g. `AddAndReadBack…` reads `ForCustomerAsync(customer.Id)` → read context must be `NewDbContext(customer.Id.Value)`), `CustomerRepositoryTests`, `HarvestEndToEndTests`, `DbSeederTests`, `InquiryRepositoryTests` (Inquiry is **unfiltered** — likely no change). For `DeleteAsync` used by admin tests: it reads `db.Leads.Where(...)` and `db.Customers.FirstOrDefault(...)`; under filters these need the tenant set or `IgnoreQueryFilters()`. If an admin test deletes cross-tenant, add `.IgnoreQueryFilters()` to `CustomerRepository.DeleteAsync`'s two reads (it is an admin operation) — make that change here and note it. Do **not** weaken any assertion; only supply the correct tenant.

**Startup seeders must be filter-safe (production code, not just tests).** These run at app boot with **no tenant** established, so any filtered query fails closed:
- `src/Coldstart.Infrastructure/DbSeeder.cs` — `SeedAsync` guards on `db.Customers.AnyAsync(ct)`. Under the `Customer` filter this returns `false` forever → it reseeds "Coldstart" every boot. Change it to `db.Customers.IgnoreQueryFilters().AnyAsync(ct)`.
- `src/Coldstart.Web/Identity/IdentitySeeder.cs` links the operator to the "Coldstart" tenant via `IProvisionCustomer.ProvisionAsync` → `CustomerRepository.GetByNameAsync` (already `IgnoreQueryFilters` from Step 2) → safe. No change, but verify the operator seeding still resolves/links the tenant with no tenant context.
- Confirm `DbSeederTests` still pass (they build the context via `PostgresFixture`; the `IgnoreQueryFilters` guard makes the seed idempotent regardless of tenant).

- [ ] **Step 5: Run**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests` → green (new isolation tests pass; existing tests updated).
Then full `dotnet test` → green. Web stays green (Task 2 resolves the tenant); Harvester paths stay green (Task 3). Report per-project totals.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Infrastructure/ColdstartDbContext.cs src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs tests/Coldstart.Infrastructure.Tests/
git commit -m "feat(tenancy): fail-closed query filters on Lead and Customer with explicit system bypasses"
```

---

### Task 5: Per-tenant dedup — composite fingerprint uniqueness

**Files:**
- Modify: `src/Coldstart.Infrastructure/Configurations/LeadConfiguration.cs` (index)
- Create: EF migration `…_LeadFingerprintPerTenant`
- Test: `tests/Coldstart.Infrastructure.Tests/TenantIsolationTests.cs` (add dedup tests)

**Interfaces:**
- Consumes: filters from Task 4; `ILeadRepository.ExistsByFingerprintAsync` (auto-scoped by the Lead filter).

- [ ] **Step 1: Write the failing dedup test**

Add to `tests/Coldstart.Infrastructure.Tests/TenantIsolationTests.cs`:

```csharp
    [Fact]
    public async Task SameFingerprint_CanBelongToTwoTenants_AndDedupIsPerTenant()
    {
        // Arrange
        var fp = "fp-shared-" + Guid.NewGuid().ToString("N");
        Guid a, b;
        await using (var seed = fixture.NewDbContext())
        {
            var ca = Customer.Create($"A-{Guid.NewGuid():N}");
            var cb = Customer.Create($"B-{Guid.NewGuid():N}");
            seed.Customers.AddRange(ca, cb);
            await seed.SaveChangesAsync();
            a = ca.Id.Value; b = cb.Id.Value;
        }

        // Act — the same fingerprint saved under BOTH tenants must both succeed (composite unique)
        await using (var asA = fixture.NewDbContext(a))
        {
            var lead = Lead.CreateHarvested(new CustomerId(a), "Acme", Fit.Yes, "s", "b", "hn", "https://x/1", fp, 0.7, "outbound", DateTimeOffset.UtcNow);
            asA.Leads.Add(lead);
            await asA.SaveChangesAsync();
        }
        await using (var asB = fixture.NewDbContext(b))
        {
            var lead = Lead.CreateHarvested(new CustomerId(b), "Acme", Fit.Yes, "s", "b", "hn", "https://x/1", fp, 0.7, "outbound", DateTimeOffset.UtcNow);
            asB.Leads.Add(lead);
            await asB.SaveChangesAsync(); // must NOT throw a unique violation
        }

        // Assert — dedup is per-tenant
        await using var checkA = fixture.NewDbContext(a);
        (await new LeadRepository(checkA).ExistsByFingerprintAsync(fp, CancellationToken.None)).Should().BeTrue();
        await using var fresh = fixture.NewDbContext(Guid.NewGuid());
        (await new LeadRepository(fresh).ExistsByFingerprintAsync(fp, CancellationToken.None)).Should().BeFalse();
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~SameFingerprint"`
Expected: FAIL — the second save violates the still-global unique `IX_leads_Fingerprint`.

- [ ] **Step 3: Change the index to composite**

Open `src/Coldstart.Infrastructure/Configurations/LeadConfiguration.cs`, find the fingerprint unique index (currently unique on `Fingerprint` with a filter `Fingerprint <> ''`) and change it to a composite on `(CustomerId, Fingerprint)` keeping the partial filter. Example (adapt to the file's existing builder variable and column names):

```csharp
        builder.HasIndex(l => new { l.CustomerId, l.Fingerprint })
            .IsUnique()
            .HasFilter("\"Fingerprint\" <> ''");
```

Remove the old single-column unique index definition.

- [ ] **Step 4: Create the migration**

`dotnet ef` needs a startup project with the Design package. Follow the repo's established procedure (see prior migrations): temporarily reference `Microsoft.EntityFrameworkCore.Design` from `Coldstart.Web` if required, then:

```bash
dotnet ef migrations add LeadFingerprintPerTenant \
  --project src/Coldstart.Infrastructure --startup-project src/Coldstart.Web
```

Verify the generated migration drops `IX_leads_Fingerprint` and creates the composite unique index with the `Fingerprint <> ''` filter; hand-correct the `Up`/`Down` if the scaffolder misses the partial filter. Revert any temporary Design reference afterward.

> **Not live yet — existing data is disposable.** No need to preserve rows or make the migration backward-safe. If migration history gets awkward, dropping and recreating the dev DB (`dotnet ef database drop -f` then `database update`, or just letting the test fixture's `EnsureCreatedAsync` build fresh) is acceptable.

- [ ] **Step 5: Run**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~TenantIsolation"` → all PASS (`EnsureCreatedAsync` in the fixture builds the schema from the current model, so the composite index applies).
Then full `dotnet test` → green (report per-project totals).

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Infrastructure/Configurations/LeadConfiguration.cs src/Coldstart.Infrastructure/Migrations/ tests/Coldstart.Infrastructure.Tests/TenantIsolationTests.cs
git commit -m "feat(tenancy): per-tenant lead dedup via composite (CustomerId, Fingerprint) unique index"
```

---

## Post-implementation (manual, outside the plan)

- Recreate the dev DB (not live — data is disposable): `dotnet ef database update` (or drop + recreate) so the composite index + current schema apply cleanly.
- Live smoke: sign in as each of the two existing accounts; confirm each sees only its own leads and the admin console still lists all companies; run the harvester and confirm dedup is per-tenant.

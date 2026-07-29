# Admin API + CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An API-key-protected `/api/admin/*` HTTP surface in Coldstart.Web plus a thin `coldstart` CLI so the operator (and Claude via Bash) can administer the deployed app from a terminal.

**Architecture:** Minimal APIs on one `/api/admin` route group, gated by a constant-time `X-Admin-Key` endpoint filter (fail closed: no configured key → 404). The UserManager logic buried in the `/admin` Razor page is extracted into an `internal AdminUserService` (Web layer — it is Identity-bound) shared by the page and the endpoints. The CLI (`src/Coldstart.Cli`) is a pure HTTP client — zero project references, `COLDSTART_URL` + `COLDSTART_ADMIN_KEY` env vars.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, ASP.NET Identity, EF Core/Npgsql, xUnit + FluentAssertions + NSubstitute, Testcontainers (existing `ColdstartWebFactory` / `PostgresFixture`).

**Spec:** `docs/superpowers/specs/2026-07-29-admin-api-cli-design.md`

## Global Constraints

- Target framework `net10.0`; `ImplicitUsings` + `Nullable` enabled (match existing csproj style).
- Tests: xUnit + FluentAssertions (7.*) + NSubstitute; naming `MethodName_StateUnderTest_ExpectedBehavior`; strict AAA with `// Arrange` / `// Act` / `// Assert` comments; 1 happy + 2 sad paths minimum per class.
- Hexagonal rules: nothing Identity/ASP.NET-bound goes into `Coldstart.Application` or `Coldstart.Domain`. `internal` over `public` for implementations; `public record` for DTOs.
- `CancellationToken` on every IO-bound signature.
- Async all the way — no `.Result`/`.Wait()`.
- Commit messages: conventional (`feat(admin): ...`), **no Co-Authored-By trailer, never push** (the user pushes).
- Build/test commands from repo root: `dotnet build Coldstart.sln` / `dotnet test tests/<Project> --filter "FullyQualifiedName~<Class>"`. The Web/Infrastructure test suites need Docker running (Testcontainers).
- JSON responses from minimal APIs use the default web casing (camelCase) — the CLI parses camelCase property names.

---

### Task 1: Admin API key gate + `/api/admin/ping`

The fail-closed authentication core everything else hangs on, plus deploy config and the ADR.

**Files:**
- Create: `src/Coldstart.Web/Api/AdminApiOptions.cs`
- Create: `src/Coldstart.Web/Api/AdminApiKeyFilter.cs`
- Create: `src/Coldstart.Web/Api/AdminEndpoints.cs`
- Modify: `src/Coldstart.Web/Program.cs` (options binding + `MapAdminApi()`)
- Modify: `tests/Coldstart.Web.Tests/ColdstartWebFactory.cs` (configure a test key)
- Create: `tests/Coldstart.Web.Tests/AdminApiAuthTests.cs`
- Modify: `deploy/.env.prod.example`, `docker-compose.prod.yml`
- Create: `docs/adrs/0001-admin-api-key-auth.md` (new `docs/adrs/` directory)

**Interfaces:**
- Consumes: nothing new.
- Produces: `AdminEndpoints.MapAdminApi(this IEndpointRouteBuilder app)` in namespace `Coldstart.Web.Api` — later tasks add endpoints inside its route group variable `group`. `ColdstartWebFactory.TestAdminKey` (`public const string`, value `"test-admin-key"`) — later tests send it as the `X-Admin-Key` header. `AdminApiKeyFilter.HeaderName` (`public const string`, value `"X-Admin-Key"`).

- [ ] **Step 1: Write the failing tests**

`tests/Coldstart.Web.Tests/AdminApiAuthTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Coldstart.Web.Tests;

public sealed class AdminApiAuthTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Ping_WithCorrectKey_ShouldReturn200()
    {
        // Arrange
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", ColdstartWebFactory.TestAdminKey);

        // Act
        var response = await client.GetAsync("/api/admin/ping");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ok");
    }

    [Fact]
    public async Task Ping_WithoutKeyHeader_ShouldReturn401()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/admin/ping");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ping_WithWrongKey_ShouldReturn401()
    {
        // Arrange
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", "not-the-key");

        // Act
        var response = await client.GetAsync("/api/admin/ping");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ping_WhenNoKeyConfigured_ShouldReturn404()
    {
        // Arrange — fail closed: an unset key means the whole API is off, not open.
        using var noKeyFactory = factory.WithWebHostBuilder(b => b.UseSetting("AdminApi:Key", ""));
        using var client = noKeyFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", ColdstartWebFactory.TestAdminKey);

        // Act
        var response = await client.GetAsync("/api/admin/ping");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

In `ColdstartWebFactory.cs` add the constant and setting:

```csharp
public const string TestAdminKey = "test-admin-key";
```

and inside `ConfigureWebHost`, next to the other `UseSetting` calls:

```csharp
builder.UseSetting("AdminApi:Key", TestAdminKey);
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~AdminApiAuthTests"`
Expected: FAIL — 404s where 200/401 are expected (no endpoints mapped yet). Compile error first until Step 3 files exist; that also counts as the failing state.

- [ ] **Step 3: Implement options, filter, group**

`src/Coldstart.Web/Api/AdminApiOptions.cs`:

```csharp
namespace Coldstart.Web.Api;

public sealed class AdminApiOptions
{
    public string? Key { get; set; }
}
```

`src/Coldstart.Web/Api/AdminApiKeyFilter.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Coldstart.Web.Api;

// Gate for /api/admin/*: constant-time X-Admin-Key check against AdminApi:Key.
// Fail closed: no configured key -> 404 (the feature is off, not open).
internal sealed class AdminApiKeyFilter(IOptions<AdminApiOptions> options) : IEndpointFilter
{
    public const string HeaderName = "X-Admin-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configured = options.Value.Key;
        if (string.IsNullOrWhiteSpace(configured))
            return Results.NotFound();

        var presented = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (presented.Length == 0 || !FixedTimeEquals(presented, configured))
            return Results.Unauthorized();

        return await next(context);
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
```

`src/Coldstart.Web/Api/AdminEndpoints.cs`:

```csharp
namespace Coldstart.Web.Api;

internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").AddEndpointFilter<AdminApiKeyFilter>();

        group.MapGet("/ping", () => Results.Ok(new { status = "ok" }));

        return app;
    }
}
```

If `AddEndpointFilter<AdminApiKeyFilter>()` does not resolve on the group (overload availability), use the two-generic form: `app.MapGroup("/api/admin").AddEndpointFilter<RouteGroupBuilder, AdminApiKeyFilter>()`.

In `Program.cs`, add with the other `Configure<...>` calls (~line 23):

```csharp
builder.Services.Configure<Coldstart.Web.Api.AdminApiOptions>(builder.Configuration.GetSection("AdminApi"));
```

and after `app.MapHealthChecks("/health");` (line 94):

```csharp
app.MapAdminApi();
```

with `using Coldstart.Web.Api;` added to the top of `Program.cs`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~AdminApiAuthTests"`
Expected: 4/4 PASS.

- [ ] **Step 5: Deploy config + ADR**

`deploy/.env.prod.example` — append:

```bash
# Admin API key for /api/admin/* (the coldstart CLI). Long + random, e.g.:
#   openssl rand -base64 32
# Leave blank to disable the admin API entirely (routes return 404).
ADMIN_API_KEY=
```

`docker-compose.prod.yml` — in the `web` service `environment` block, after the `DataProtection__KeyPath` line:

```yaml
      # Admin API (/api/admin/* + coldstart CLI). Soft-degrade: unset → the admin
      # API is off (404), the web app is unaffected.
      AdminApi__Key: ${ADMIN_API_KEY:-}
```

`docs/adrs/0001-admin-api-key-auth.md`:

```markdown
# 0001 — Static API key for the admin API

**Date:** 2026-07-29
**Status:** Accepted

## Context

Coldstart needs terminal administration (operator + Claude via Bash) of the deployed
app: `/api/admin/*` endpoints consumed by a thin CLI. There is exactly one operator
(SuperAdmin). Existing auth is ASP.NET Identity cookies; there is no bearer/JWT setup.

## Decision

A single static API key sent as `X-Admin-Key`, verified by an endpoint filter on the
`/api/admin` route group with a constant-time comparison against `AdminApi:Key`
(user-secrets in dev, `deploy/.env.prod` in production). Fail closed: when no key is
configured, every route in the group returns 404. Transport security comes from
Caddy-terminated HTTPS. Cookie login for a CLI (antiforgery/session handling) and JWT
(issuer/signing/expiry machinery) were rejected as needless for a single operator.

## Consequences

**Positive:** trivially scriptable (`curl -H "X-Admin-Key: ..."`); no token expiry to
babysit; one config value; the filter is swappable for a real authentication scheme
without touching endpoint code if staff are ever added.

**Negative:** one shared secret with full admin power — rotation means changing one env
var and restarting; no per-action audit identity (there is only one operator); the key
must never be logged or committed.
```

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Web/Api/ src/Coldstart.Web/Program.cs tests/Coldstart.Web.Tests/AdminApiAuthTests.cs tests/Coldstart.Web.Tests/ColdstartWebFactory.cs deploy/.env.prod.example docker-compose.prod.yml docs/adrs/
git commit -m "feat(admin-api): fail-closed X-Admin-Key gate on /api/admin + ping endpoint"
```

---

### Task 2: `LeadCountsAsync` on `ICustomerRepository`

Cross-tenant lead counts for the customers overview — a single grouped query instead of N tenant-scoped reads.

**Files:**
- Modify: `src/Coldstart.Application/Ports/ICustomerRepository.cs`
- Modify: `src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs`
- Modify: `tests/Coldstart.Infrastructure.Tests/CustomerRepositoryTests.cs` (append tests)

**Interfaces:**
- Consumes: existing `ColdstartDbContext.Leads` (`DbSet<Lead>`), fail-closed tenant filter (hence `IgnoreQueryFilters`).
- Produces: `public sealed record LeadCounts(int Total, int PendingReview);` (namespace `Coldstart.Application.Ports`, same file as the interface) and `Task<IReadOnlyDictionary<Guid, LeadCounts>> LeadCountsAsync(CancellationToken ct)` on `ICustomerRepository`. Customers with zero leads are absent from the dictionary — callers use `TryGetValue`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Coldstart.Infrastructure.Tests/CustomerRepositoryTests.cs`, following that file's existing fixture pattern (`PostgresFixture`, `fx.NewDbContext()`, `new CustomerRepository(db)` — the repo is `internal`, visible via `InternalsVisibleTo`). Use unique customer names (e.g. suffix `Guid.NewGuid()`) so tests don't collide with rows from other tests sharing the container:

```csharp
[Fact]
public async Task LeadCountsAsync_WithLeadsAcrossCustomersAndStatuses_ShouldGroupPerCustomer()
{
    // Arrange
    await using var db = fx.NewDbContext();
    var repo = new CustomerRepository(db);
    var a = Customer.Create($"counts-a-{Guid.NewGuid()}");
    var b = Customer.Create($"counts-b-{Guid.NewGuid()}");
    var now = DateTimeOffset.UtcNow;
    a.AddLead("Acme", Fit.Yes, "s1", "d1", now);
    a.AddLead("Beta", Fit.Yes, "s2", "d2", now);
    a.AddLead("Gamma", Fit.Maybe, "s3", "d3", now).Approve();   // no longer PendingReview
    b.AddLead("Delta", Fit.Yes, "s4", "d4", now);
    await repo.AddAsync(a, CancellationToken.None);
    await repo.AddAsync(b, CancellationToken.None);

    // Act
    var counts = await repo.LeadCountsAsync(CancellationToken.None);

    // Assert
    counts[a.Id.Value].Should().Be(new LeadCounts(Total: 3, PendingReview: 2));
    counts[b.Id.Value].Should().Be(new LeadCounts(Total: 1, PendingReview: 1));
}

[Fact]
public async Task LeadCountsAsync_CustomerWithoutLeads_ShouldBeAbsentFromResult()
{
    // Arrange
    await using var db = fx.NewDbContext();
    var repo = new CustomerRepository(db);
    var empty = Customer.Create($"counts-empty-{Guid.NewGuid()}");
    await repo.AddAsync(empty, CancellationToken.None);

    // Act
    var counts = await repo.LeadCountsAsync(CancellationToken.None);

    // Assert
    counts.Should().NotContainKey(empty.Id.Value);
}
```

(Check `Fit.cs` for enum member names before finalizing — the harvest tests already use them; mirror those. If `Approve()` requires a recipient in this codebase's current signature, it doesn't — `Approve(EmailAddress? recipient = null)`.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~CustomerRepositoryTests"`
Expected: compile FAIL — `LeadCountsAsync`/`LeadCounts` don't exist.

- [ ] **Step 3: Implement port + repository method**

In `src/Coldstart.Application/Ports/ICustomerRepository.cs` add below the interface:

```csharp
public sealed record LeadCounts(int Total, int PendingReview);
```

and to the interface (with the other system-level members):

```csharp
// System-level (no tenant context): per-customer lead totals for the admin overview.
// Customers with zero leads are absent from the dictionary.
Task<IReadOnlyDictionary<Guid, LeadCounts>> LeadCountsAsync(CancellationToken ct);
```

In `src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs`:

```csharp
public async Task<IReadOnlyDictionary<Guid, LeadCounts>> LeadCountsAsync(CancellationToken ct)
{
    var grouped = await db.Leads.IgnoreQueryFilters()
        .GroupBy(l => l.CustomerId)
        .Select(g => new
        {
            Id = g.Key,
            Total = g.Count(),
            Pending = g.Count(l => l.Status == LeadStatus.PendingReview),
        })
        .ToListAsync(ct);
    return grouped.ToDictionary(x => x.Id.Value, x => new LeadCounts(x.Total, x.Pending));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~CustomerRepositoryTests"`
Expected: PASS (all, including pre-existing tests in the class).

- [ ] **Step 5: Full build (other ICustomerRepository implementors/mocks)**

Run: `dotnet build Coldstart.sln`
Expected: SUCCESS. If any test double implements `ICustomerRepository` explicitly (grep `: ICustomerRepository` under `tests/`), add a stub member returning `new Dictionary<Guid, LeadCounts>()`. NSubstitute mocks need no change.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Application/Ports/ICustomerRepository.cs src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs tests/
git commit -m "feat(admin-api): cross-tenant per-customer lead counts on ICustomerRepository"
```

---

### Task 3: Extract `AdminUserService` from the /admin Razor page

Pure refactor: behaviour identical, verified by the existing admin suites staying green. This is the service Tasks 4–5 consume.

**Files:**
- Create: `src/Coldstart.Web/Admin/AdminUserService.cs`
- Modify: `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml.cs` (delegate to the service)
- Modify: `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml` (only if it references the `UserRow` type by name)
- Modify: `src/Coldstart.Web/Program.cs` (register service + `AddHttpContextAccessor`)

**Interfaces:**
- Consumes: `UserManager<ApplicationUser>`, `IProvisionCustomer`, `ICustomerRepository`, `IDeleteCustomer`, `IEmailSender`, `ISearchProfileGenerator`, `IHarvestQueue`, `EmailTemplates.Invitation(company, link)`, `ColdstartRoles` (namespace `Coldstart.Web`), `LinkGenerator`, `IHttpContextAccessor`.
- Produces (namespace `Coldstart.Web.Admin`):
  - `internal enum AdminOutcome { Ok, NotFound, Forbidden, Conflict }`
  - `public sealed record AdminUserRow(string Id, string Email, string Company, string Role, string Status, int? DaysLeft, bool CanManage, Guid? CustomerId)` — public because it's the type of the public `IndexModel.Rows` property.
  - `internal sealed record AdminActionResult(AdminOutcome Outcome, string Message, string? SetPasswordLink = null, Guid? CustomerId = null)` with `public bool Succeeded => Outcome == AdminOutcome.Ok;`
  - `internal sealed record InviteRequest(string Email, string Company, string Niche, string? Info)`
  - `internal sealed class AdminUserService` with:
    - `Task<IReadOnlyList<AdminUserRow>> ListAsync(string? actorEmail, CancellationToken ct)`
    - `Task<AdminActionResult> InviteAsync(InviteRequest request, CancellationToken ct)`
    - `Task<AdminActionResult> ResendAsync(string email, CancellationToken ct)`
    - `Task<AdminActionResult> RevokeAsync(string email, CancellationToken ct)`
    - `Task<AdminActionResult> CopyLinkAsync(string email, CancellationToken ct)`
    - `Task<AdminActionResult> BlockAsync(string email, string? actorEmail, CancellationToken ct)`
    - `Task<AdminActionResult> UnblockAsync(string email, string? actorEmail, CancellationToken ct)`
    - `Task<AdminActionResult> RemoveAsync(string email, string? actorEmail, CancellationToken ct)`
  - `actorEmail` is who is acting: the page passes `User.Identity?.Name`; API-key endpoints pass `null`. Self-target guard applies only when `actorEmail` is known; SuperAdmin targets are ALWAYS refused (`Forbidden`).

- [ ] **Step 1: Baseline — run the existing admin suites**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~AdminConsoleTests|FullyQualifiedName~AdminInviteTests|FullyQualifiedName~OnboardingInviteTests"`
Expected: PASS. Record the count — the same tests must pass after the refactor.

- [ ] **Step 2: Write the service**

`src/Coldstart.Web/Admin/AdminUserService.cs` — the bodies are lifted from `Index.cshtml.cs` (lines 57–100 invite, 102–147 resend/revoke/copy, 150–192 block/unblock/remove, 211–278 helpers) with `Input.*` → `request.*`, `StatusMessage` → returned `Message`, and `Url.Page` → `LinkGenerator`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Coldstart.Application.Customers;
using Coldstart.Application.Harvest;
using Coldstart.Application.Ports;
using Coldstart.Infrastructure.Identity;
using Coldstart.Web.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Web.Admin;

internal enum AdminOutcome { Ok, NotFound, Forbidden, Conflict }

public sealed record AdminUserRow(
    string Id, string Email, string Company, string Role, string Status,
    int? DaysLeft, bool CanManage, Guid? CustomerId);

internal sealed record AdminActionResult(
    AdminOutcome Outcome, string Message, string? SetPasswordLink = null, Guid? CustomerId = null)
{
    public bool Succeeded => Outcome == AdminOutcome.Ok;
}

internal sealed record InviteRequest(string Email, string Company, string Niche, string? Info);

// SuperAdmin user administration, shared by the /admin Razor page and /api/admin.
// Web layer on purpose: it is ASP.NET-Identity-bound and must not sink into Application.
// actorEmail = who is acting (null for API-key calls): the self-target guard applies only
// when known; SuperAdmin targets are always refused.
internal sealed class AdminUserService(
    UserManager<ApplicationUser> users,
    IProvisionCustomer provision,
    ICustomerRepository customers,
    IDeleteCustomer deleteCustomer,
    IEmailSender email,
    ISearchProfileGenerator profileGen,
    IHarvestQueue harvestQueue,
    LinkGenerator links,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<IReadOnlyList<AdminUserRow>> ListAsync(string? actorEmail, CancellationToken ct)
    {
        var all = await customers.AllAsync(ct);
        var names = all.ToDictionary(c => c.Id.Value, c => c.Name);

        var rows = new List<AdminUserRow>();
        foreach (var u in await users.Users.ToListAsync(ct))
        {
            var roles = await users.GetRolesAsync(u);
            var role = roles.FirstOrDefault() ?? "";
            var isSuper = role == ColdstartRoles.SuperAdmin;
            var status = !u.EmailConfirmed ? "Pending"
                : u.LockoutEnd is { } le && le > DateTimeOffset.UtcNow ? "Blocked" : "Active";
            var company = u.CustomerId is { } cid && names.TryGetValue(cid, out var n) ? n : "—";
            rows.Add(new AdminUserRow(u.Id, u.Email!, company, role, status,
                DaysLeft(u.InvitedAt),
                CanManage: !isSuper && !string.Equals(u.Email, actorEmail, StringComparison.OrdinalIgnoreCase),
                u.CustomerId));
        }
        return rows.OrderBy(r => r.Company).ThenBy(r => r.Email).ToList();
    }

    public async Task<AdminActionResult> InviteAsync(InviteRequest request, CancellationToken ct)
    {
        if (await users.FindByEmailAsync(request.Email) is not null)
            return new(AdminOutcome.Conflict, "That person already has an account.");

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = false,
            InvitedAt = DateTimeOffset.UtcNow,
        };
        var created = await users.CreateAsync(user, RandomPassword());
        if (!created.Succeeded)
            return new(AdminOutcome.Conflict, string.Join(" ", created.Errors.Select(e => e.Description)));

        await users.AddToRoleAsync(user, ColdstartRoles.CompanyAdmin);

        SearchProfile? profile = null;
        try { profile = await profileGen.GenerateAsync(new SearchProfileRequest(request.Niche, request.Info ?? ""), ct); }
        catch { /* generation must never block the invite */ }

        user.CustomerId = await provision.ProvisionAsync(
            request.Company, new CompanyProfileSeed(request.Niche, request.Info, profile), ct);
        await users.UpdateAsync(user);

        var link = await SendInviteAsync(user, request.Company, ct);
        harvestQueue.Enqueue(new HarvestJob(user.CustomerId.Value));

        return new(AdminOutcome.Ok, $"Invitation sent to {request.Email}.", link, user.CustomerId);
    }

    public async Task<AdminActionResult> ResendAsync(string email, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null || user.EmailConfirmed || !await IsCompanyAdminAsync(user))
            return new(AdminOutcome.NotFound, "Nothing to resend for that account.");

        var company = await CompanyNameAsync(user, ct);
        var link = await SendInviteAsync(user, company, ct);
        return new(AdminOutcome.Ok, $"Invitation re-sent to {email}.", link);
    }

    public async Task<AdminActionResult> RevokeAsync(string email, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null || user.EmailConfirmed || !await IsCompanyAdminAsync(user))
            return new(AdminOutcome.Conflict, "Only pending invites can be revoked.");

        var customerId = user.CustomerId;
        var result = await users.DeleteAsync(user);
        if (!result.Succeeded)
            return new(AdminOutcome.Conflict, $"Could not remove {email}.");

        if (customerId is { } cid) await deleteCustomer.DeleteAsync(cid, ct);
        return new(AdminOutcome.Ok, $"Invitation to {email} revoked.");
    }

    public async Task<AdminActionResult> CopyLinkAsync(string email, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null || user.EmailConfirmed || !await IsCompanyAdminAsync(user))
            return new(AdminOutcome.NotFound, "Only pending invites have a set-password link.");

        return new(AdminOutcome.Ok, $"Set-password link for {email}.", await BuildSetPasswordLinkAsync(user));
    }

    public async Task<AdminActionResult> BlockAsync(string email, string? actorEmail, CancellationToken ct)
    {
        var (user, error) = await GuardedAsync(email, actorEmail);
        if (user is null) return error!;

        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await users.UpdateSecurityStampAsync(user);   // invalidate any live cookie
        return new(AdminOutcome.Ok, $"{user.Email} is blocked.");
    }

    public async Task<AdminActionResult> UnblockAsync(string email, string? actorEmail, CancellationToken ct)
    {
        var (user, error) = await GuardedAsync(email, actorEmail);
        if (user is null) return error!;

        await users.SetLockoutEndDateAsync(user, null);
        return new(AdminOutcome.Ok, $"{user.Email} is active again.");
    }

    public async Task<AdminActionResult> RemoveAsync(string email, string? actorEmail, CancellationToken ct)
    {
        var (user, error) = await GuardedAsync(email, actorEmail);
        if (user is null) return error!;

        var customerId = user.CustomerId;
        var result = await users.DeleteAsync(user);
        if (!result.Succeeded)
            return new(AdminOutcome.Conflict, "Could not remove that account.");

        if (customerId is { } cid) await deleteCustomer.DeleteAsync(cid, ct);
        return new(AdminOutcome.Ok, "Account removed.");
    }

    // Returns the target only if it is safe to act on: exists, is not a SuperAdmin,
    // and is not the actor themselves.
    private async Task<(ApplicationUser? User, AdminActionResult? Error)> GuardedAsync(string email, string? actorEmail)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
            return (null, new AdminActionResult(AdminOutcome.NotFound, "No such account."));
        if (await users.IsInRoleAsync(user, ColdstartRoles.SuperAdmin)
            || string.Equals(user.Email, actorEmail, StringComparison.OrdinalIgnoreCase))
            return (null, new AdminActionResult(AdminOutcome.Forbidden, "That account can't be modified."));
        return (user, null);
    }

    private async Task<bool> IsCompanyAdminAsync(ApplicationUser user) =>
        await users.IsInRoleAsync(user, ColdstartRoles.CompanyAdmin);

    private async Task<string> CompanyNameAsync(ApplicationUser user, CancellationToken ct)
    {
        if (user.CustomerId is not { } cid) return "your company";
        var all = await customers.AllAsync(ct);
        return all.FirstOrDefault(c => c.Id.Value == cid)?.Name ?? "your company";
    }

    private async Task<string> SendInviteAsync(ApplicationUser user, string company, CancellationToken ct)
    {
        var link = await BuildSetPasswordLinkAsync(user);
        var content = EmailTemplates.Invitation(company, link);
        await email.SendAsync(user.Email!, content.Subject, content.TextBody, content.HtmlBody, ct);
        return link;
    }

    private async Task<string> BuildSetPasswordLinkAsync(ApplicationUser user)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var ctx = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No active HTTP request to build the link from.");
        return links.GetUriByPage(ctx, "/Account/ResetPassword", handler: null,
                values: new { area = "Identity", code, email = user.Email })
            ?? throw new InvalidOperationException("Could not build the set-password link.");
    }

    private static int? DaysLeft(DateTimeOffset? invitedAt) =>
        invitedAt is { } t ? Math.Max(0, 7 - (int)(DateTimeOffset.UtcNow - t).TotalDays) : null;

    private static string RandomPassword() =>
        "Aa1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
}
```

Register in `Program.cs` (next to the `AdminApiOptions` binding from Task 1):

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Coldstart.Web.Admin.AdminUserService>();
```

- [ ] **Step 3: Slim the page to delegation**

Rewrite `Index.cshtml.cs`: constructor becomes `(AdminUserService admin, UserManager<ApplicationUser> users, IInquiryRepository inquiries)`; delete the private helpers that moved (`GuardTargetAsync`, `IsCompanyAdminAsync`, `CompanyNameAsync`, `SendInviteAsync`, `BuildSetPasswordLinkAsync`, `DaysLeft`, `RandomPassword`) and the `UserRow` record; `Rows` becomes `IReadOnlyList<AdminUserRow>` (from `Coldstart.Web.Admin`). Handler bodies:

```csharp
public async Task<IActionResult> OnPostInviteAsync(CancellationToken ct)
{
    if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }
    var result = await admin.InviteAsync(new InviteRequest(Input.Email, Input.Company, Input.Niche, Input.Info), ct);
    if (!result.Succeeded)
    {
        ModelState.AddModelError("Input.Email", result.Message);
        await LoadAsync(ct);
        return Page();
    }
    StatusMessage = result.Message;
    Input = new InputModel();
    await LoadAsync(ct);
    return Page();
}

public async Task<IActionResult> OnPostResendAsync(string email, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(email)) { StatusMessage = "No account specified."; return await RenderAsync(ct); }
    StatusMessage = (await admin.ResendAsync(email, ct)).Message;
    return await RenderAsync(ct);
}

public async Task<IActionResult> OnPostRevokeAsync(string email, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(email)) { StatusMessage = "No account specified."; return await RenderAsync(ct); }
    StatusMessage = (await admin.RevokeAsync(email, ct)).Message;
    return await RenderAsync(ct);
}

public async Task<IActionResult> OnPostCopyLinkAsync(string email, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(email)) { StatusMessage = "No account specified."; return await RenderAsync(ct); }
    var result = await admin.CopyLinkAsync(email, ct);
    if (result.Succeeded) CopyLink = (email, result.SetPasswordLink!);
    return await RenderAsync(ct);
}

public async Task<IActionResult> OnPostBlockAsync(string id, CancellationToken ct)
{
    StatusMessage = await ActByIdAsync(id, email => admin.BlockAsync(email, User.Identity?.Name, ct));
    return await RenderAsync(ct);
}

public async Task<IActionResult> OnPostUnblockAsync(string id, CancellationToken ct)
{
    StatusMessage = await ActByIdAsync(id, email => admin.UnblockAsync(email, User.Identity?.Name, ct));
    return await RenderAsync(ct);
}

public async Task<IActionResult> OnPostRemoveAsync(string id, CancellationToken ct)
{
    StatusMessage = await ActByIdAsync(id, email => admin.RemoveAsync(email, User.Identity?.Name, ct));
    return await RenderAsync(ct);
}

// The row buttons post the Identity user id; the service is keyed by email.
private async Task<string> ActByIdAsync(string id, Func<string, Task<AdminActionResult>> action)
{
    var user = await users.FindByIdAsync(id);
    if (user?.Email is not { } email) return "No such account.";
    return (await action(email)).Message;
}

private async Task LoadAsync(CancellationToken ct)
{
    Rows = await admin.ListAsync(User.Identity?.Name, ct);
    Inquiries = (await inquiries.RecentAsync(20, ct))
        .Select(i => new InquiryRow(i.Id.Value, i.Name, i.Email.Value, i.Niche, i.WhatYouSell, i.Message, i.CreatedAt))
        .ToList();
}
```

`OnGetAsync` and `RenderAsync` stay as they are. Check `Index.cshtml` for any `IndexModel.UserRow` type reference and switch it to `AdminUserRow`; the property names on the row are unchanged so bindings keep working.

- [ ] **Step 4: Run the full Web suite to verify identical behaviour**

Run: `dotnet test tests/Coldstart.Web.Tests`
Expected: PASS — same totals as Step 1's baseline. If an existing test asserts an exact status message that changed (e.g. unknown-account resend), restore the original message text in the service rather than editing the test.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Web/ tests/
git commit -m "refactor(admin): extract AdminUserService from the /admin page (shared with the API)"
```

---

### Task 4: `GET /api/admin/customers` + `GET /api/admin/stats`

**Files:**
- Create: `src/Coldstart.Web/Api/AdminDtos.cs`
- Modify: `src/Coldstart.Web/Api/AdminEndpoints.cs`
- Create: `tests/Coldstart.Web.Tests/AdminApiReadTests.cs`

**Interfaces:**
- Consumes: `AdminUserService.ListAsync(null, ct)` (Task 3), `ICustomerRepository.AllAsync` / `.LeadCountsAsync` (Task 2), `HealthCheckService`.
- Produces (namespace `Coldstart.Web.Api`, all serialized camelCase):
  - `public sealed record AdminUserDto(string Email, string Role, string Status, int? InviteDaysLeft);`
  - `public sealed record AdminCustomerDto(Guid Id, string Name, string Plan, DateTimeOffset? PlanRenewsAt, bool HasStripeCustomer, bool HasSubscription, string HarvestStatus, DateTimeOffset? LastHarvestAt, int LeadsTotal, int LeadsPendingReview, IReadOnlyList<AdminUserDto> Users);`
  - `public sealed record AdminStatsDto(string Database, int Customers, int Users, int PendingInvites, int BlockedUsers, Dictionary<string, int> HarvestStatuses);`

- [ ] **Step 1: Write the failing tests**

`tests/Coldstart.Web.Tests/AdminApiReadTests.cs` — a private helper builds a keyed client; data comes from `TestAuth.CreateUserAsync` (provisions a tenant named after the email):

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Coldstart.Web.Tests;

public sealed class AdminApiReadTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", ColdstartWebFactory.TestAdminKey);
        return client;
    }

    [Fact]
    public async Task Customers_WithProvisionedTenant_ShouldListItWithItsUser()
    {
        // Arrange
        await TestAuth.CreateUserAsync(factory, "read-tenant@example.com", "Passw0rd!x", role: "CompanyAdmin");
        using var client = Client();

        // Act
        var response = await client.GetAsync("/api/admin/customers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var match = doc.RootElement.EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("name").GetString() == "read-tenant@example.com");
        match.ValueKind.Should().Be(JsonValueKind.Object);
        match.GetProperty("plan").GetString().Should().Be("Free");
        match.GetProperty("leadsTotal").GetInt32().Should().Be(0);
        match.GetProperty("users").EnumerateArray()
            .Select(u => u.GetProperty("email").GetString())
            .Should().Contain("read-tenant@example.com");
    }

    [Fact]
    public async Task Customers_WithoutKey_ShouldReturn401()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/admin/customers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Stats_WithHealthyDb_ShouldReportHealthyAndCounts()
    {
        // Arrange
        await TestAuth.CreateUserAsync(factory, "stats-tenant@example.com", "Passw0rd!x", role: "CompanyAdmin");
        using var client = Client();

        // Act
        var stats = await client.GetFromJsonAsync<JsonElement>("/api/admin/stats");

        // Assert
        stats.GetProperty("database").GetString().Should().Be("Healthy");
        stats.GetProperty("customers").GetInt32().Should().BeGreaterThan(0);
        stats.GetProperty("users").GetInt32().Should().BeGreaterThan(0);
    }
}
```

(The `GetFromJsonAsync` call needs no extra package — `System.Net.Http.Json` ships in the shared framework.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~AdminApiReadTests"`
Expected: FAIL — 404 (routes not mapped).

- [ ] **Step 3: Implement DTOs + endpoints**

`src/Coldstart.Web/Api/AdminDtos.cs`:

```csharp
namespace Coldstart.Web.Api;

public sealed record AdminUserDto(string Email, string Role, string Status, int? InviteDaysLeft);

public sealed record AdminCustomerDto(
    Guid Id, string Name, string Plan, DateTimeOffset? PlanRenewsAt,
    bool HasStripeCustomer, bool HasSubscription,
    string HarvestStatus, DateTimeOffset? LastHarvestAt,
    int LeadsTotal, int LeadsPendingReview,
    IReadOnlyList<AdminUserDto> Users);

public sealed record AdminStatsDto(
    string Database, int Customers, int Users, int PendingInvites, int BlockedUsers,
    Dictionary<string, int> HarvestStatuses);
```

Add to `MapAdminApi` in `AdminEndpoints.cs` (new usings: `Coldstart.Application.Ports`, `Coldstart.Web.Admin`, `Microsoft.Extensions.Diagnostics.HealthChecks`):

```csharp
group.MapGet("/customers", async (
    ICustomerRepository customers, AdminUserService admin, CancellationToken ct) =>
{
    var all = await customers.AllAsync(ct);
    var counts = await customers.LeadCountsAsync(ct);
    var userRows = await admin.ListAsync(actorEmail: null, ct);

    var dtos = all.Select(c =>
    {
        counts.TryGetValue(c.Id.Value, out var lc);
        return new AdminCustomerDto(
            c.Id.Value, c.Name, c.Plan.ToString(), c.PlanRenewsAt,
            c.StripeCustomerId is not null, c.StripeSubscriptionId is not null,
            c.HarvestStatus.ToString(), c.LastHarvestAt,
            lc?.Total ?? 0, lc?.PendingReview ?? 0,
            userRows.Where(u => u.CustomerId == c.Id.Value)
                .Select(u => new AdminUserDto(u.Email, u.Role, u.Status, u.DaysLeft))
                .ToList());
    }).ToList();
    return Results.Ok(dtos);
});

group.MapGet("/stats", async (
    HealthCheckService health, ICustomerRepository customers, AdminUserService admin,
    CancellationToken ct) =>
{
    var report = await health.CheckHealthAsync(ct);
    var all = await customers.AllAsync(ct);
    var rows = await admin.ListAsync(actorEmail: null, ct);
    return Results.Ok(new AdminStatsDto(
        report.Status.ToString(),
        all.Count,
        rows.Count,
        rows.Count(r => r.Status == "Pending"),
        rows.Count(r => r.Status == "Blocked"),
        all.GroupBy(c => c.HarvestStatus.ToString()).ToDictionary(g => g.Key, g => g.Count())));
});
```

Note: `LeadCounts` is a record (reference type), so `counts.TryGetValue(...) ; lc?.Total ?? 0` works with `out var lc` being null when absent.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~AdminApiReadTests"`
Expected: 3/3 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Web/Api/ tests/Coldstart.Web.Tests/AdminApiReadTests.cs
git commit -m "feat(admin-api): customers overview + stats endpoints"
```

---

### Task 5: User-action endpoints (invite / resend / revoke / block / unblock / remove)

**Files:**
- Modify: `src/Coldstart.Web/Api/AdminEndpoints.cs`
- Modify: `src/Coldstart.Web/Api/AdminDtos.cs` (add `InviteRequestDto`)
- Create: `tests/Coldstart.Web.Tests/AdminApiUserActionTests.cs`

**Interfaces:**
- Consumes: `AdminUserService` (Task 3) — all action methods with `actorEmail: null`.
- Produces: `public sealed record InviteRequestDto(string? Email, string? Company, string? Niche, string? Info);` Routes (all under the keyed group): `POST /invites` (201/400/409), `POST /invites/{email}/resend` (200/404), `DELETE /invites/{email}` (200/409), `POST /users/{email}/block` (200/403/404), `POST /users/{email}/unblock` (200/403/404), `DELETE /users/{email}` (200/403/404). Failures are `ProblemDetails` with the service message as `title`.

- [ ] **Step 1: Write the failing tests**

`tests/Coldstart.Web.Tests/AdminApiUserActionTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Coldstart.Web.Tests;

public sealed class AdminApiUserActionTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", ColdstartWebFactory.TestAdminKey);
        return client;
    }

    [Fact]
    public async Task Invite_WithValidBody_ShouldCreateTenantSendEmailAndEnqueueHarvest()
    {
        // Arrange
        using var client = Client();
        var before = factory.HarvestQueue.Jobs.Count;

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/invites",
            new { email = "api-invite@example.com", company = "Api Invite BV", niche = "HR SaaS" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("customerId").GetGuid().Should().NotBeEmpty();
        body.GetProperty("setPasswordLink").GetString().Should().Contain("ResetPassword");
        factory.Email.Sent.Should().Contain(m => m.To == "api-invite@example.com");
        factory.HarvestQueue.Jobs.Count.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task Invite_WhenEmailAlreadyExists_ShouldReturn409()
    {
        // Arrange
        using var client = Client();
        await client.PostAsJsonAsync("/api/admin/invites",
            new { email = "api-dup@example.com", company = "Dup BV", niche = "n" });

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/invites",
            new { email = "api-dup@example.com", company = "Dup BV", niche = "n" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Invite_WithMissingCompany_ShouldReturn400()
    {
        // Arrange
        using var client = Client();

        // Act
        var response = await client.PostAsJsonAsync("/api/admin/invites",
            new { email = "api-invalid@example.com", niche = "n" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Block_ExistingUser_ShouldLockOut()
    {
        // Arrange
        await TestAuth.CreateUserAsync(factory, "api-block@example.com", "Passw0rd!x", role: "CompanyAdmin");
        using var client = Client();

        // Act
        var response = await client.PostAsync("/api/admin/users/api-block@example.com/block", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync("api-block@example.com");
        user!.LockoutEnd.Should().NotBeNull();
        user.LockoutEnd!.Value.Should().BeAfter(DateTimeOffset.UtcNow.AddYears(100));
    }

    [Fact]
    public async Task Block_SuperAdmin_ShouldReturn403()
    {
        // Arrange — the seeded SuperAdmin must be untouchable via the API.
        using var client = Client();

        // Act
        var response = await client.PostAsync($"/api/admin/users/{ColdstartRoles.SuperAdminEmail}/block", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Remove_UnknownUser_ShouldReturn404()
    {
        // Arrange
        using var client = Client();

        // Act
        var response = await client.DeleteAsync("/api/admin/users/nobody@example.com");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Revoke_PendingInvite_ShouldDeleteUserAndTenant()
    {
        // Arrange
        using var client = Client();
        await client.PostAsJsonAsync("/api/admin/invites",
            new { email = "api-revoke@example.com", company = "Revoke BV", niche = "n" });

        // Act
        var response = await client.DeleteAsync("/api/admin/invites/api-revoke@example.com");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var customers = await client.GetFromJsonAsync<JsonElement>("/api/admin/customers");
        customers.EnumerateArray().Select(c => c.GetProperty("name").GetString())
            .Should().NotContain("Revoke BV");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~AdminApiUserActionTests"`
Expected: FAIL — 404s.

- [ ] **Step 3: Implement endpoints**

Add `InviteRequestDto` to `AdminDtos.cs`:

```csharp
public sealed record InviteRequestDto(string? Email, string? Company, string? Niche, string? Info);
```

Add to `MapAdminApi` (the `{email}` route values arrive URL-decoded):

```csharp
group.MapPost("/invites", async (InviteRequestDto body, AdminUserService admin, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Company)
        || string.IsNullOrWhiteSpace(body.Niche))
        return Results.Problem(statusCode: 400, title: "email, company and niche are required.");

    var result = await admin.InviteAsync(new InviteRequest(body.Email, body.Company, body.Niche, body.Info), ct);
    return result.Succeeded
        ? Results.Created("/api/admin/customers",
            new { customerId = result.CustomerId, setPasswordLink = result.SetPasswordLink, message = result.Message })
        : ToProblem(result);
});

group.MapPost("/invites/{email}/resend", async (string email, AdminUserService admin, CancellationToken ct) =>
    ToHttp(await admin.ResendAsync(email, ct)));

group.MapDelete("/invites/{email}", async (string email, AdminUserService admin, CancellationToken ct) =>
    ToHttp(await admin.RevokeAsync(email, ct)));

group.MapPost("/users/{email}/block", async (string email, AdminUserService admin, CancellationToken ct) =>
    ToHttp(await admin.BlockAsync(email, actorEmail: null, ct)));

group.MapPost("/users/{email}/unblock", async (string email, AdminUserService admin, CancellationToken ct) =>
    ToHttp(await admin.UnblockAsync(email, actorEmail: null, ct)));

group.MapDelete("/users/{email}", async (string email, AdminUserService admin, CancellationToken ct) =>
    ToHttp(await admin.RemoveAsync(email, actorEmail: null, ct)));
```

and the private helpers on `AdminEndpoints`:

```csharp
private static IResult ToHttp(AdminActionResult result) =>
    result.Succeeded
        ? Results.Ok(new { message = result.Message, setPasswordLink = result.SetPasswordLink })
        : ToProblem(result);

private static IResult ToProblem(AdminActionResult result) => result.Outcome switch
{
    AdminOutcome.NotFound => Results.Problem(statusCode: 404, title: result.Message),
    AdminOutcome.Forbidden => Results.Problem(statusCode: 403, title: result.Message),
    _ => Results.Problem(statusCode: 409, title: result.Message),
};
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~AdminApiUserActionTests"`
Expected: 7/7 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Web/Api/ tests/Coldstart.Web.Tests/AdminApiUserActionTests.cs
git commit -m "feat(admin-api): invite/resend/revoke/block/unblock/remove endpoints"
```

---

### Task 6: Harvest endpoints

**Files:**
- Modify: `src/Coldstart.Web/Api/AdminEndpoints.cs`
- Create: `tests/Coldstart.Web.Tests/AdminApiHarvestTests.cs`

**Interfaces:**
- Consumes: `IHarvestQueue.Enqueue(HarvestJob)`, `NightlyHarvestScheduler.EnqueueDueAsync(IServiceProvider, int, CancellationToken)` (public static), `HarvestScheduleOptions.TopUpThreshold`, `ICustomerRepository.AllAsync`.
- Produces: `POST /api/admin/harvest/{customerId:guid}` → 202 `{ enqueued: "<name>" }` / 404; `POST /api/admin/harvest/due` → 200 `{ enqueued: <count> }`.

- [ ] **Step 1: Write the failing tests**

`tests/Coldstart.Web.Tests/AdminApiHarvestTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Coldstart.Web.Tests;

public sealed class AdminApiHarvestTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", ColdstartWebFactory.TestAdminKey);
        return client;
    }

    [Fact]
    public async Task HarvestCustomer_ExistingCustomer_ShouldEnqueueJobAndReturn202()
    {
        // Arrange
        using var client = Client();
        var invite = await client.PostAsJsonAsync("/api/admin/invites",
            new { email = "api-harvest@example.com", company = "Harvest BV", niche = "n" });
        var customerId = (await invite.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("customerId").GetGuid();
        var before = factory.HarvestQueue.Jobs.Count;

        // Act
        var response = await client.PostAsync($"/api/admin/harvest/{customerId}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.HarvestQueue.Jobs.Skip(before).Should().Contain(j => j.CustomerId == customerId);
    }

    [Fact]
    public async Task HarvestCustomer_UnknownCustomer_ShouldReturn404()
    {
        // Arrange
        using var client = Client();

        // Act
        var response = await client.PostAsync($"/api/admin/harvest/{Guid.NewGuid()}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HarvestDue_WithNoFullPlanCustomers_ShouldReturnZero()
    {
        // Arrange — factory tenants are Free-plan except the seeded Coldstart tenant,
        // which has HarvestStatus.None and no profile keywords but IS Full plan; assert
        // only that the endpoint answers with a count.
        using var client = Client();

        // Act
        var response = await client.PostAsync("/api/admin/harvest/due", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("enqueued").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~AdminApiHarvestTests"`
Expected: FAIL — 404s.

- [ ] **Step 3: Implement endpoints**

Add to `MapAdminApi` (new usings: `Coldstart.Application.Harvest`, `Coldstart.Infrastructure.Harvest`, `Microsoft.Extensions.Options`; the `IServiceProvider` parameter resolves to the request's scoped provider):

```csharp
group.MapPost("/harvest/{customerId:guid}", async (
    Guid customerId, ICustomerRepository customers, IHarvestQueue queue, CancellationToken ct) =>
{
    var customer = (await customers.AllAsync(ct)).FirstOrDefault(c => c.Id.Value == customerId);
    if (customer is null)
        return Results.Problem(statusCode: 404, title: "No such customer.");

    queue.Enqueue(new HarvestJob(customerId));
    return Results.Accepted($"/api/admin/customers", new { enqueued = customer.Name });
});

group.MapPost("/harvest/due", async (
    IServiceProvider services, IOptions<HarvestScheduleOptions> schedule, CancellationToken ct) =>
{
    var count = await NightlyHarvestScheduler.EnqueueDueAsync(services, schedule.Value.TopUpThreshold, ct);
    return Results.Ok(new { enqueued = count });
});
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~AdminApiHarvestTests"`
Expected: 3/3 PASS. Then the full Web suite: `dotnet test tests/Coldstart.Web.Tests` — all green.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Web/Api/AdminEndpoints.cs tests/Coldstart.Web.Tests/AdminApiHarvestTests.cs
git commit -m "feat(admin-api): on-demand harvest trigger + run-due-now endpoints"
```

---

### Task 7: CLI project — read commands (`ping`, `stats`, `customers`)

**Files:**
- Create: `src/Coldstart.Cli/Coldstart.Cli.csproj`, `src/Coldstart.Cli/Program.cs`, `src/Coldstart.Cli/AdminApiClient.cs`, `src/Coldstart.Cli/CommandRunner.cs`, `src/Coldstart.Cli/Table.cs`
- Create: `tests/Coldstart.Cli.Tests/Coldstart.Cli.Tests.csproj`, `tests/Coldstart.Cli.Tests/StubHandler.cs`, `tests/Coldstart.Cli.Tests/CommandRunnerTests.cs`
- Modify: `Coldstart.sln` (via `dotnet sln add`)

**Interfaces:**
- Consumes: the HTTP API from Tasks 1/4 (camelCase JSON).
- Produces (namespace `Coldstart.Cli`, all `internal`, `InternalsVisibleTo` the test project):
  - `internal sealed record ApiResult(int Status, string Body)` with `public bool Ok => Status is >= 200 and < 300;`
  - `internal sealed class AdminApiClient(HttpClient http)` — `Task<ApiResult> GetAsync(string path, CancellationToken ct = default)`, `PostAsync(string path, object? body = null, ...)`, `DeleteAsync(string path, ...)`.
  - `internal static class CommandRunner` — `Task<int> RunAsync(string[] args, AdminApiClient api, TextWriter stdout, TextWriter stderr)`; exit codes 0 ok / 1 API-HTTP error / 2 usage error.
  - `internal static class Table` — `string Render(string[] headers, IReadOnlyList<string[]> rows)` (two-space column padding).
  - Task 8 extends `CommandRunner.RunAsync`'s switch with the mutating commands.

- [ ] **Step 1: Create the projects and wire the solution**

```bash
mkdir -p src/Coldstart.Cli tests/Coldstart.Cli.Tests
```

`src/Coldstart.Cli/Coldstart.Cli.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>coldstart</AssemblyName>
    <RootNamespace>Coldstart.Cli</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Coldstart.Cli.Tests" />
  </ItemGroup>

</Project>
```

`tests/Coldstart.Cli.Tests/Coldstart.Cli.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="FluentAssertions" Version="7.*" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Coldstart.Cli\Coldstart.Cli.csproj" />
  </ItemGroup>

</Project>
```

```bash
dotnet sln Coldstart.sln add src/Coldstart.Cli/Coldstart.Cli.csproj tests/Coldstart.Cli.Tests/Coldstart.Cli.Tests.csproj
```

- [ ] **Step 2: Write the failing tests**

`tests/Coldstart.Cli.Tests/StubHandler.cs`:

```csharp
using System.Net;
using System.Text;

namespace Coldstart.Cli.Tests;

// Canned-response HttpMessageHandler: the CLI is tested end-to-end minus the network.
internal sealed class StubHandler(Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> respond)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        var (status, body) = respond(request);
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }

    internal static AdminApiClient Client(Func<HttpRequestMessage, (HttpStatusCode, string)> respond,
        out StubHandler handler)
    {
        handler = new StubHandler(respond);
        return new AdminApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://test.local") });
    }
}
```

`tests/Coldstart.Cli.Tests/CommandRunnerTests.cs`:

```csharp
using System.Net;
using FluentAssertions;

namespace Coldstart.Cli.Tests;

public sealed class CommandRunnerTests
{
    private const string CustomersJson =
        """
        [{"id":"11111111-1111-1111-1111-111111111111","name":"Acme BV","plan":"Full",
          "planRenewsAt":null,"hasStripeCustomer":true,"hasSubscription":true,
          "harvestStatus":"Done","lastHarvestAt":"2026-07-28T04:00:00+00:00",
          "leadsTotal":12,"leadsPendingReview":3,
          "users":[{"email":"kim@acme.example","role":"CompanyAdmin","status":"Active","inviteDaysLeft":null}]}]
        """;

    [Fact]
    public async Task RunAsync_Customers_ShouldRenderTableWithCompanyAndLeads()
    {
        // Arrange
        var api = StubHandler.Client(_ => (HttpStatusCode.OK, CustomersJson), out _);
        var stdout = new StringWriter();

        // Act
        var exit = await CommandRunner.RunAsync(["customers"], api, stdout, new StringWriter());

        // Assert
        exit.Should().Be(0);
        stdout.ToString().Should().Contain("Acme BV").And.Contain("3/12").And.Contain("kim@acme.example");
    }

    [Fact]
    public async Task RunAsync_CustomersWithJsonFlag_ShouldPrintRawBody()
    {
        // Arrange
        var api = StubHandler.Client(_ => (HttpStatusCode.OK, CustomersJson), out _);
        var stdout = new StringWriter();

        // Act
        var exit = await CommandRunner.RunAsync(["customers", "--json"], api, stdout, new StringWriter());

        // Assert
        exit.Should().Be(0);
        stdout.ToString().Should().Contain("\"leadsTotal\":12");
    }

    [Fact]
    public async Task RunAsync_WhenServerRejectsKey_ShouldPrintErrorAndExit1()
    {
        // Arrange
        var api = StubHandler.Client(_ => (HttpStatusCode.Unauthorized, ""), out _);
        var stderr = new StringWriter();

        // Act
        var exit = await CommandRunner.RunAsync(["stats"], api, new StringWriter(), stderr);

        // Assert
        exit.Should().Be(1);
        stderr.ToString().Should().Contain("401");
    }

    [Fact]
    public async Task RunAsync_UnknownCommand_ShouldPrintUsageAndExit2()
    {
        // Arrange
        var api = StubHandler.Client(_ => (HttpStatusCode.OK, "{}"), out var handler);
        var stderr = new StringWriter();

        // Act
        var exit = await CommandRunner.RunAsync(["frobnicate"], api, new StringWriter(), stderr);

        // Assert
        exit.Should().Be(2);
        stderr.ToString().Should().Contain("Usage");
        handler.Requests.Should().BeEmpty();
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Coldstart.Cli.Tests`
Expected: compile FAIL — `CommandRunner`/`AdminApiClient` don't exist.

- [ ] **Step 4: Implement the CLI**

`src/Coldstart.Cli/AdminApiClient.cs`:

```csharp
using System.Text;
using System.Text.Json;

namespace Coldstart.Cli;

internal sealed record ApiResult(int Status, string Body)
{
    public bool Ok => Status is >= 200 and < 300;
}

internal sealed class AdminApiClient(HttpClient http)
{
    public Task<ApiResult> GetAsync(string path, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, path, null, ct);

    public Task<ApiResult> PostAsync(string path, object? body = null, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, path, body, ct);

    public Task<ApiResult> DeleteAsync(string path, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, path, null, ct);

    private async Task<ApiResult> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, ct);
        return new ApiResult((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
    }
}
```

`src/Coldstart.Cli/Table.cs`:

```csharp
using System.Text;

namespace Coldstart.Cli;

internal static class Table
{
    public static string Render(string[] headers, IReadOnlyList<string[]> rows)
    {
        var widths = headers
            .Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length)))
            .ToArray();
        string Line(string[] cells) =>
            string.Join("  ", cells.Select((c, i) => c.PadRight(widths[i]))).TrimEnd();

        var sb = new StringBuilder();
        sb.AppendLine(Line(headers));
        foreach (var row in rows) sb.AppendLine(Line(row));
        return sb.ToString().TrimEnd();
    }
}
```

`src/Coldstart.Cli/CommandRunner.cs` (read commands only — Task 8 extends the switch):

```csharp
using System.Text.Json;

namespace Coldstart.Cli;

internal static class CommandRunner
{
    private const string Usage =
        """
        Usage: coldstart <command> [options]

        Commands:
          ping                         Check the admin API is up and the key works
          stats                        Health + customer/user/invite counts
          customers                    Companies, users, plans, leads, harvest state

        Options:
          --json                       Print the raw API response (read commands)
        """;

    public static async Task<int> RunAsync(string[] args, AdminApiClient api, TextWriter stdout, TextWriter stderr)
    {
        var json = args.Contains("--json");
        var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

        try
        {
            return positional switch
            {
                ["ping"] => await ReadAsync(api.GetAsync("/api/admin/ping"), json, stdout, stderr, _ => "ok"),
                ["stats"] => await ReadAsync(api.GetAsync("/api/admin/stats"), json, stdout, stderr, RenderStats),
                ["customers"] => await ReadAsync(api.GetAsync("/api/admin/customers"), json, stdout, stderr, RenderCustomers),
                _ => UsageError(stderr),
            };
        }
        catch (HttpRequestException ex)
        {
            await stderr.WriteLineAsync($"Cannot reach the Coldstart API: {ex.Message}");
            return 1;
        }
    }

    private static int UsageError(TextWriter stderr)
    {
        stderr.WriteLine(Usage);
        return 2;
    }

    private static async Task<int> ReadAsync(Task<ApiResult> call, bool json,
        TextWriter stdout, TextWriter stderr, Func<JsonElement, string> render)
    {
        var result = await call;
        if (!result.Ok)
        {
            await stderr.WriteLineAsync($"HTTP {result.Status}{(result.Body.Length > 0 ? ": " + result.Body : "")}");
            return 1;
        }
        if (json)
        {
            await stdout.WriteLineAsync(result.Body);
            return 0;
        }
        using var doc = JsonDocument.Parse(result.Body);
        await stdout.WriteLineAsync(render(doc.RootElement));
        return 0;
    }

    private static string RenderStats(JsonElement root) => Table.Render(
        ["DATABASE", "CUSTOMERS", "USERS", "PENDING INVITES", "BLOCKED"],
        [[
            root.GetProperty("database").GetString() ?? "",
            root.GetProperty("customers").GetInt32().ToString(),
            root.GetProperty("users").GetInt32().ToString(),
            root.GetProperty("pendingInvites").GetInt32().ToString(),
            root.GetProperty("blockedUsers").GetInt32().ToString(),
        ]]);

    private static string RenderCustomers(JsonElement root)
    {
        var rows = new List<string[]>();
        foreach (var c in root.EnumerateArray())
        {
            var users = string.Join(", ", c.GetProperty("users").EnumerateArray()
                .Select(u => $"{u.GetProperty("email").GetString()} ({u.GetProperty("status").GetString()})"));
            var lastRun = c.GetProperty("lastHarvestAt").ValueKind == JsonValueKind.Null
                ? "—" : c.GetProperty("lastHarvestAt").GetString()!;
            rows.Add([
                c.GetProperty("name").GetString() ?? "",
                c.GetProperty("plan").GetString() ?? "",
                $"{c.GetProperty("leadsPendingReview").GetInt32()}/{c.GetProperty("leadsTotal").GetInt32()}",
                c.GetProperty("harvestStatus").GetString() ?? "",
                lastRun,
                users,
            ]);
        }
        return Table.Render(["COMPANY", "PLAN", "LEADS", "HARVEST", "LAST RUN", "USERS"], rows);
    }
}
```

`src/Coldstart.Cli/Program.cs`:

```csharp
using Coldstart.Cli;

var url = Environment.GetEnvironmentVariable("COLDSTART_URL") ?? "http://localhost:5000";
var key = Environment.GetEnvironmentVariable("COLDSTART_ADMIN_KEY");
if (string.IsNullOrWhiteSpace(key))
{
    Console.Error.WriteLine("COLDSTART_ADMIN_KEY is not set. Export it (and COLDSTART_URL for remote targets) first.");
    return 2;
}

using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.Add("X-Admin-Key", key);
return await CommandRunner.RunAsync(args, new AdminApiClient(http), Console.Out, Console.Error);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Coldstart.Cli.Tests`
Expected: 4/4 PASS. Also `dotnet build Coldstart.sln` — SUCCESS.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Cli/ tests/Coldstart.Cli.Tests/ Coldstart.sln
git commit -m "feat(cli): coldstart CLI skeleton + ping/stats/customers read commands"
```

---

### Task 8: CLI action commands + docs

**Files:**
- Modify: `src/Coldstart.Cli/CommandRunner.cs`
- Modify: `tests/Coldstart.Cli.Tests/CommandRunnerTests.cs` (append tests)
- Modify: `README.md` (Admin CLI section)

**Interfaces:**
- Consumes: Task 5/6 endpoints; Task 7's `ReadAsync`/`UsageError` helpers.
- Produces: commands `invite <email> --company <name> --niche <text> [--info <text>]`, `resend <email>`, `revoke <email>`, `block <email>`, `unblock <email>`, `remove <email> --yes`, `harvest <company-name-or-guid>`, `harvest --due`. `remove` without `--yes` refuses with exit 2 (non-interactive safety). `harvest <name>` resolves the customer id via `GET /customers` (case-insensitive name match).

- [ ] **Step 1: Write the failing tests**

Append to `CommandRunnerTests.cs`:

```csharp
[Fact]
public async Task RunAsync_Invite_ShouldPostBodyAndPrintMessage()
{
    // Arrange
    var api = StubHandler.Client(req =>
        req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/admin/invites"
            ? (HttpStatusCode.Created, """{"customerId":"22222222-2222-2222-2222-222222222222","setPasswordLink":"https://x/reset","message":"Invitation sent to new@x.com."}""")
            : (HttpStatusCode.NotFound, ""), out var handler);
    var stdout = new StringWriter();

    // Act
    var exit = await CommandRunner.RunAsync(
        ["invite", "new@x.com", "--company", "New BV", "--niche", "HR SaaS"],
        api, stdout, new StringWriter());

    // Assert
    exit.Should().Be(0);
    stdout.ToString().Should().Contain("Invitation sent").And.Contain("https://x/reset");
    var sent = await handler.Requests.Single().Content!.ReadAsStringAsync();
    sent.Should().Contain("New BV").And.Contain("HR SaaS");
}

[Fact]
public async Task RunAsync_InviteWithoutCompany_ShouldExit2WithoutCalling()
{
    // Arrange
    var api = StubHandler.Client(_ => (HttpStatusCode.OK, "{}"), out var handler);
    var stderr = new StringWriter();

    // Act
    var exit = await CommandRunner.RunAsync(["invite", "new@x.com"], api, new StringWriter(), stderr);

    // Assert
    exit.Should().Be(2);
    handler.Requests.Should().BeEmpty();
}

[Fact]
public async Task RunAsync_RemoveWithoutYes_ShouldRefuseAndExit2()
{
    // Arrange
    var api = StubHandler.Client(_ => (HttpStatusCode.OK, "{}"), out var handler);
    var stderr = new StringWriter();

    // Act
    var exit = await CommandRunner.RunAsync(["remove", "kim@acme.example"], api, new StringWriter(), stderr);

    // Assert
    exit.Should().Be(2);
    stderr.ToString().Should().Contain("--yes");
    handler.Requests.Should().BeEmpty();
}

[Fact]
public async Task RunAsync_HarvestByCompanyName_ShouldResolveIdThenPost()
{
    // Arrange
    var api = StubHandler.Client(req => req.RequestUri!.AbsolutePath switch
    {
        "/api/admin/customers" => (HttpStatusCode.OK, CustomersJson),
        "/api/admin/harvest/11111111-1111-1111-1111-111111111111" => (HttpStatusCode.Accepted, """{"enqueued":"Acme BV"}"""),
        _ => (HttpStatusCode.NotFound, ""),
    }, out var handler);
    var stdout = new StringWriter();

    // Act
    var exit = await CommandRunner.RunAsync(["harvest", "acme bv"], api, stdout, new StringWriter());

    // Assert
    exit.Should().Be(0);
    handler.Requests.Should().HaveCount(2);
    stdout.ToString().Should().Contain("Acme BV");
}

[Fact]
public async Task RunAsync_HarvestUnknownCompany_ShouldExit1()
{
    // Arrange
    var api = StubHandler.Client(_ => (HttpStatusCode.OK, CustomersJson), out _);
    var stderr = new StringWriter();

    // Act
    var exit = await CommandRunner.RunAsync(["harvest", "nope inc"], api, new StringWriter(), stderr);

    // Assert
    exit.Should().Be(1);
    stderr.ToString().Should().Contain("No customer named");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Coldstart.Cli.Tests`
Expected: new tests FAIL (exit 2/usage for every new command).

- [ ] **Step 3: Implement the commands**

Extend the `positional switch` in `CommandRunner.RunAsync` (before the `_` arm) and add helpers:

```csharp
["invite", var email, ..] when GetOption(args, "--company") is { } company
        && GetOption(args, "--niche") is { } niche =>
    await ActAsync(api.PostAsync("/api/admin/invites",
        new { email, company, niche, info = GetOption(args, "--info") }), stdout, stderr),
["invite", ..] => UsageError(stderr, "invite requires <email> --company <name> --niche <text>."),

["resend", var email] => await ActAsync(api.PostAsync($"/api/admin/invites/{Uri.EscapeDataString(email)}/resend"), stdout, stderr),
["revoke", var email] => await ActAsync(api.DeleteAsync($"/api/admin/invites/{Uri.EscapeDataString(email)}"), stdout, stderr),
["block", var email] => await ActAsync(api.PostAsync($"/api/admin/users/{Uri.EscapeDataString(email)}/block"), stdout, stderr),
["unblock", var email] => await ActAsync(api.PostAsync($"/api/admin/users/{Uri.EscapeDataString(email)}/unblock"), stdout, stderr),
["remove", var email] when args.Contains("--yes") =>
    await ActAsync(api.DeleteAsync($"/api/admin/users/{Uri.EscapeDataString(email)}"), stdout, stderr),
["remove", ..] => UsageError(stderr, "remove deletes the user AND their tenant data — re-run with --yes."),

["harvest"] when args.Contains("--due") =>
    await ActAsync(api.PostAsync("/api/admin/harvest/due"), stdout, stderr),
["harvest", var target] => await HarvestAsync(target, api, stdout, stderr),
```

```csharp
// --option value (the token after the flag)
private static string? GetOption(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

private static int UsageError(TextWriter stderr, string? message = null)
{
    if (message is not null) stderr.WriteLine(message);
    stderr.WriteLine(Usage);
    return 2;
}

// Mutations: print the API's message (or raw body) on success.
private static async Task<int> ActAsync(Task<ApiResult> call, TextWriter stdout, TextWriter stderr)
{
    var result = await call;
    if (!result.Ok)
    {
        await stderr.WriteLineAsync($"HTTP {result.Status}{(result.Body.Length > 0 ? ": " + result.Body : "")}");
        return 1;
    }
    await stdout.WriteLineAsync(result.Body);
    return 0;
}

private static async Task<int> HarvestAsync(string target, AdminApiClient api, TextWriter stdout, TextWriter stderr)
{
    if (!Guid.TryParse(target, out var id))
    {
        var customers = await api.GetAsync("/api/admin/customers");
        if (!customers.Ok)
        {
            await stderr.WriteLineAsync($"HTTP {customers.Status}: {customers.Body}");
            return 1;
        }
        using var doc = System.Text.Json.JsonDocument.Parse(customers.Body);
        var match = doc.RootElement.EnumerateArray().FirstOrDefault(c =>
            string.Equals(c.GetProperty("name").GetString(), target, StringComparison.OrdinalIgnoreCase));
        if (match.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            await stderr.WriteLineAsync($"No customer named '{target}'. Run `coldstart customers` to see names.");
            return 1;
        }
        id = match.GetProperty("id").GetGuid();
    }
    return await ActAsync(api.PostAsync($"/api/admin/harvest/{id}"), stdout, stderr);
}
```

Update the `Usage` string to list all commands:

```
  invite <email> --company <name> --niche <text> [--info <text>]
  resend <email>               Re-send a pending invite
  revoke <email>               Revoke a pending invite (deletes user + tenant)
  block <email> / unblock <email>
  remove <email> --yes         Delete a user AND their tenant data
  harvest <company|id>         Enqueue a harvest for one customer
  harvest --due                Run the nightly top-up sweep now
```

Note on `["invite", var email, ..]`: the rest-pattern is required (not just for flags) because `positional` strips `--company` etc. but keeps their VALUES — `invite a@x.com --company "New BV"` yields positional `["invite", "a@x.com", "New BV"]`. Option values only ever appear after the email, so the rest-pattern absorbs them.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Coldstart.Cli.Tests`
Expected: 9/9 PASS.

- [ ] **Step 5: Smoke it against the real app (manual, optional but recommended)**

```bash
dotnet run --project src/Coldstart.Web &   # or the existing dev-run workflow
COLDSTART_URL=http://localhost:5000 COLDSTART_ADMIN_KEY=<dev key from user-secrets> \
  dotnet run --project src/Coldstart.Cli -- stats
```

(Set a dev key first: `dotnet user-secrets set "AdminApi:Key" "dev-admin-key" --project src/Coldstart.Web`.)

- [ ] **Step 6: README + commit**

Add to `README.md` (near the ops/deploy notes):

```markdown
## Admin CLI

Terminal administration of a running Coldstart instance (local or production) over
the key-protected admin API:

    export COLDSTART_URL=https://getcoldstart.nl     # default: http://localhost:5000
    export COLDSTART_ADMIN_KEY=...                    # AdminApi:Key / ADMIN_API_KEY

    dotnet run --project src/Coldstart.Cli -- customers
    dotnet run --project src/Coldstart.Cli -- invite kim@acme.example --company "Acme BV" --niche "HR SaaS"
    dotnet run --project src/Coldstart.Cli -- harvest "Acme BV"
    dotnet run --project src/Coldstart.Cli -- stats --json

Every read command takes `--json` for machine consumption. `remove` requires `--yes`.
Publish a standalone binary with `dotnet publish src/Coldstart.Cli -c Release`.
```

```bash
git add src/Coldstart.Cli/CommandRunner.cs tests/Coldstart.Cli.Tests/CommandRunnerTests.cs README.md
git commit -m "feat(cli): invite/resend/revoke/block/unblock/remove/harvest commands"
```

---

## Final verification (after all tasks)

- [ ] `dotnet build Coldstart.sln` — 0 warnings, 0 errors.
- [ ] `dotnet test Coldstart.sln` — full suite green (Docker running for Testcontainers).
- [ ] Manual: `/admin` page still works in the browser (invite form + row actions) — the refactor kept behaviour identical.

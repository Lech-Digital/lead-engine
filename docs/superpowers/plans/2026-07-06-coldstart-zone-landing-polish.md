# Coldstart Zone & Landing Polish (Stage A) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Landing "Log in" reachable without scrolling; an editable company profile on the customer; a branded `/app` dashboard shell with the profile summary + leads.

**Architecture:** Add a `CompanyProfile` value object owned by `Customer` (EF owned-type) with a `UpdateProfile` behaviour; Application use cases `IGetCompanyProfile`/`IUpdateCompanyProfile` + a `CompanyProfileView` read-model (Web stays entity-free); a `/app/profile` Razor edit page; restructure `/app` into an app-bar + profile summary + the Stage-4 leads grid; a persistent hero "Log in" link on the landing.

**Tech Stack:** .NET 10, Razor Pages, EF Core 10 (Npgsql/Postgres 17), xUnit + NSubstitute + FluentAssertions 7.*, Testcontainers.PostgreSql.

## Global Constraints

- TFM `net10.0`; build **0 warnings**.
- Hexagonal: Web sees Application read-models/input records (`CompanyProfileView`/`CompanyProfileInput`/`LeadSummary`), never Domain entities (Identity `UserManager`/`ApplicationUser` in Web UI allowed).
- DDD: `CompanyProfile` is a `record`; `Customer` mutates only via `UpdateProfile` (no public setter).
- CS-DATA: repo methods async + `CancellationToken`, return `Task<T>`.
- CS-MOD: impls `internal`; `[InternalsVisibleTo]` test-only (present).
- EF Core pinned **10.0.4**; Testcontainers ctor `new PostgreSqlBuilder("postgres:17")`.
- Testing: strict three-phase AAA; Testcontainers for DB-backed; full solution green after each task.
- Commit messages: **no `Co-Authored-By`/"Generated with" trailer** (project policy).
- Builds on `dotnet-port`. Repo-root `NuGet.Config` required; Docker running for integration tests. Public registration stays live (invitations are Stage B).

---

## File Structure

```
src/Coldstart.Domain/CompanyProfile.cs                         (new value object)
src/Coldstart.Domain/Customer.cs                               (add Profile + UpdateProfile)
src/Coldstart.Application/Ports/ICustomerRepository.cs         (add GetAsync + UpdateAsync)
src/Coldstart.Application/Customers/CompanyProfileInput.cs     (new)
src/Coldstart.Application/Customers/CompanyProfileView.cs      (new)
src/Coldstart.Application/Customers/IGetCompanyProfile.cs + GetCompanyProfile.cs
src/Coldstart.Application/Customers/IUpdateCompanyProfile.cs + UpdateCompanyProfile.cs
src/Coldstart.Application/DependencyInjection.cs               (register the two use cases)
src/Coldstart.Infrastructure/Configurations/CustomerConfiguration.cs (OwnsOne profile)
src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs (GetAsync + UpdateAsync)
src/Coldstart.Infrastructure/Migrations/                       (AddCompanyProfile)
src/Coldstart.Web/Pages/Index.cshtml                           (hero "Log in" link)
src/Coldstart.Web/Areas/App/Pages/Index.cshtml(.cs)            (app-bar + profile summary)
src/Coldstart.Web/Areas/App/Pages/Profile.cshtml(.cs)          (new edit page)
src/Coldstart.Web/wwwroot/css/site.css                         (app-bar + profile styles)
tests: PublicPagesTests, CustomerTests(profile), CustomerRepositoryTests, Update/GetCompanyProfileTests, CustomerZoneTests
```

---

### Task 1: Landing — persistent "Log in" link in the hero

**Files:**
- Modify: `src/Coldstart.Web/Pages/Index.cshtml`, `src/Coldstart.Web/wwwroot/css/site.css`
- Test: `tests/Coldstart.Web.Tests/PublicPagesTests.cs` (add)

**Interfaces:** none new. Produces a hero header link (`class="hero-login"`) reachable without scrolling.

- [ ] **Step 1: Write the failing test**

Add to `tests/Coldstart.Web.Tests/PublicPagesTests.cs`:

```csharp
    [Fact]
    public async Task GetLanding_HasHeroLoginLink_NotOnlyInStatusbar()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var html = await client.GetStringAsync("/");

        // Assert
        html.Should().Contain("hero-login");                     // the persistent, no-scroll login link
        html.Should().Contain("/Identity/Account/Login");
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests --filter GetLanding_HasHeroLoginLink`
Expected: FAIL — no `hero-login` marker yet.

- [ ] **Step 3: Add the hero login link**

In `src/Coldstart.Web/Pages/Index.cshtml`, the hero has `<a class="brand" href="/">…logo…</a>`. Wrap that brand row so the login link sits top-right of the hero. Replace the brand anchor line with:

```html
        <div class="hero-top">
          <a class="brand" href="/" aria-label="Coldstart home"><img src="/logo.svg" alt="Coldstart" width="150" height="40" style="display:block;height:40px;width:auto;"></a>
          @if (User.Identity?.IsAuthenticated == true)
          {
              <a class="hero-login" href="/app">Customer zone →</a>
          }
          else
          {
              <a class="hero-login" href="/Identity/Account/Login">Log in</a>
          }
        </div>
```

Append to `site.css`:

```css
  .hero-top { display: flex; align-items: center; justify-content: space-between; gap: 16px; margin-bottom: 28px; }
  .hero-top .brand { margin-bottom: 0; }
  .hero-login { font-family: var(--mono); font-size: 0.74rem; text-transform: uppercase; letter-spacing: 0.08em; font-weight: 700; text-decoration: none; color: var(--ink); background: var(--cream-2); border: 1.5px solid var(--line-2); border-radius: 8px; padding: 8px 14px; box-shadow: 0 3px 0 var(--line-2); white-space: nowrap; }
  .hero-login:hover { transform: translateY(1px); box-shadow: 0 2px 0 var(--line-2); }
```

(The existing `.brand { margin-bottom: 28px }` is overridden inside `.hero-top`.)

- [ ] **Step 4: Run to green, build, commit**

Run: `dotnet test tests/Coldstart.Web.Tests --filter GetLanding_HasHeroLoginLink` → PASS; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Web/Pages/Index.cshtml src/Coldstart.Web/wwwroot/css/site.css tests/Coldstart.Web.Tests/PublicPagesTests.cs
git commit -m "feat(landing): persistent hero Log in / Customer zone link (no scroll needed)"
```

---

### Task 2: CompanyProfile value object + Customer.UpdateProfile + EF + migration

**Files:**
- Create: `src/Coldstart.Domain/CompanyProfile.cs`
- Modify: `src/Coldstart.Domain/Customer.cs`, `src/Coldstart.Application/Ports/ICustomerRepository.cs`, `src/Coldstart.Infrastructure/Configurations/CustomerConfiguration.cs`, `src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs`
- Create migration: `AddCompanyProfile`
- Test: `tests/Coldstart.Domain.Tests/CustomerProfileTests.cs`, `tests/Coldstart.Infrastructure.Tests/CustomerRepositoryTests.cs` (add)

**Interfaces:**
- Produces:
  - `record CompanyProfile(string? CompanyName, string? Address, string? VatNumber, string? KvkNumber, string? Niche, string? Website, string? ContactEmail)` with `static CompanyProfile Empty`.
  - `Customer.Profile` (`CompanyProfile`, default `Empty`) + `void UpdateProfile(CompanyProfile profile)`.
  - `ICustomerRepository.GetAsync(CustomerId id, CancellationToken ct) : Task<Customer?>`; `UpdateAsync(Customer customer, CancellationToken ct) : Task`.

- [ ] **Step 1: Write the failing domain test**

`tests/Coldstart.Domain.Tests/CustomerProfileTests.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class CustomerProfileTests
{
    [Fact]
    public void NewCustomer_HasEmptyProfile()
    {
        // Act
        var customer = Customer.Create("sam@acme.com");

        // Assert
        customer.Profile.Should().Be(CompanyProfile.Empty);
    }

    [Fact]
    public void UpdateProfile_ReplacesTheProfile()
    {
        // Arrange
        var customer = Customer.Create("sam@acme.com");
        var profile = new CompanyProfile("Acme Ltd", "1 Main St", "NL123", "KVK9", "B2B SaaS", "acme.com", "hi@acme.com");

        // Act
        customer.UpdateProfile(profile);

        // Assert
        customer.Profile.Should().Be(profile);
        customer.Profile.CompanyName.Should().Be("Acme Ltd");
    }

    [Fact]
    public void UpdateProfile_WithNull_ResetsToEmpty()
    {
        // Arrange
        var customer = Customer.Create("sam@acme.com");
        customer.UpdateProfile(new CompanyProfile("Acme", null, null, null, null, null, null));

        // Act
        customer.UpdateProfile(null!);

        // Assert
        customer.Profile.Should().Be(CompanyProfile.Empty);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Domain.Tests --filter CustomerProfileTests`
Expected: FAIL — `CompanyProfile`/`Profile` missing.

- [ ] **Step 3: Implement the value object + Customer changes**

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
    string? ContactEmail)
{
    public static CompanyProfile Empty { get; } = new(null, null, null, null, null, null, null);
}
```

In `src/Coldstart.Domain/Customer.cs` add the property (after `Name`) and the behaviour:

```csharp
    public CompanyProfile Profile { get; private set; } = CompanyProfile.Empty;
```
```csharp
    public void UpdateProfile(CompanyProfile profile) => Profile = profile ?? CompanyProfile.Empty;
```

- [ ] **Step 4: Run the domain test to green**

Run: `dotnet test tests/Coldstart.Domain.Tests --filter CustomerProfileTests`
Expected: PASS.

- [ ] **Step 5: Add repo port methods + EF owned-type mapping + impl**

`ICustomerRepository.cs` add:

```csharp
    Task<Customer?> GetAsync(CustomerId id, CancellationToken ct);
    Task UpdateAsync(Customer customer, CancellationToken ct);
```

In `CustomerConfiguration.cs` `Configure`, map the owned profile:

```csharp
        builder.OwnsOne(c => c.Profile, p =>
        {
            p.Property(x => x.CompanyName).HasColumnName("ProfileCompanyName");
            p.Property(x => x.Address).HasColumnName("ProfileAddress");
            p.Property(x => x.VatNumber).HasColumnName("ProfileVatNumber");
            p.Property(x => x.KvkNumber).HasColumnName("ProfileKvkNumber");
            p.Property(x => x.Niche).HasColumnName("ProfileNiche");
            p.Property(x => x.Website).HasColumnName("ProfileWebsite");
            p.Property(x => x.ContactEmail).HasColumnName("ProfileContactEmail");
        });
        builder.Navigation(c => c.Profile).IsRequired();   // Profile is never null (defaults to Empty)
```

In `CustomerRepository.cs` add:

```csharp
    public async Task<Customer?> GetAsync(CustomerId id, CancellationToken ct) =>
        await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task UpdateAsync(Customer customer, CancellationToken ct)
    {
        db.Customers.Update(customer);
        await db.SaveChangesAsync(ct);
    }
```

- [ ] **Step 6: Add the repo round-trip test**

Add to `tests/Coldstart.Infrastructure.Tests/CustomerRepositoryTests.cs`:

```csharp
    [Fact]
    public async Task Profile_RoundTripsThroughOwnedType()
    {
        // Arrange
        await using var write = fixture.NewDbContext();
        var customer = Customer.Create("sam@acme.com");
        customer.UpdateProfile(new CompanyProfile("Acme Ltd", "1 Main St", "NL123", "KVK9", "B2B SaaS", "acme.com", "hi@acme.com"));
        new CustomerRepository(write);   // (repo uses db directly)
        write.Customers.Add(customer);
        await write.SaveChangesAsync();

        // Act
        await using var read = fixture.NewDbContext();
        var found = await new CustomerRepository(read).GetAsync(customer.Id, CancellationToken.None);

        // Assert
        found.Should().NotBeNull();
        found!.Profile.CompanyName.Should().Be("Acme Ltd");
        found.Profile.VatNumber.Should().Be("NL123");
        found.Profile.ContactEmail.Should().Be("hi@acme.com");
    }
```

- [ ] **Step 7: Create the migration**

```bash
dotnet ef migrations add AddCompanyProfile -p src/Coldstart.Infrastructure -s src/Coldstart.Infrastructure -o Migrations
```
Verify it adds the 7 nullable `Profile*` columns to `customers`, nothing else.

- [ ] **Step 8: Run tests to green, build, commit**

Run: `docker compose up -d db && dotnet test tests/Coldstart.Domain.Tests tests/Coldstart.Infrastructure.Tests` (per-project) → PASS; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Domain src/Coldstart.Application src/Coldstart.Infrastructure tests
git commit -m "feat(profile): CompanyProfile owned by Customer + repo Get/Update + migration"
```

---

### Task 3: Application use cases (get + update profile)

**Files:**
- Create: `src/Coldstart.Application/Customers/CompanyProfileInput.cs`, `CompanyProfileView.cs`, `IGetCompanyProfile.cs`, `GetCompanyProfile.cs`, `IUpdateCompanyProfile.cs`, `UpdateCompanyProfile.cs`
- Modify: `src/Coldstart.Application/DependencyInjection.cs`
- Test: `tests/Coldstart.Application.Tests/CompanyProfileUseCasesTests.cs`

**Interfaces:**
- Consumes: `ICustomerRepository.GetAsync`/`UpdateAsync` (Task 2), `CompanyProfile`.
- Produces:
  - `record CompanyProfileInput(string? CompanyName, string? Address, string? VatNumber, string? KvkNumber, string? Niche, string? Website, string? ContactEmail)`
  - `record CompanyProfileView(string? CompanyName, string? Address, string? VatNumber, string? KvkNumber, string? Niche, string? Website, string? ContactEmail) { bool IsEmpty }`
  - `IGetCompanyProfile.ForCustomerAsync(Guid customerId, CancellationToken ct) : Task<CompanyProfileView>`
  - `IUpdateCompanyProfile.UpdateAsync(Guid customerId, CompanyProfileInput input, CancellationToken ct) : Task`

- [ ] **Step 1: Write the failing use-case tests**

`tests/Coldstart.Application.Tests/CompanyProfileUseCasesTests.cs`:

```csharp
using Coldstart.Application.Customers;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class CompanyProfileUseCasesTests
{
    [Fact]
    public async Task Update_LoadsCustomer_AppliesProfile_Saves()
    {
        // Arrange
        var customer = Customer.Create("sam@acme.com");
        var repo = Substitute.For<ICustomerRepository>();
        repo.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new UpdateCompanyProfile(repo);
        var input = new CompanyProfileInput("Acme Ltd", "1 Main St", "NL1", "KVK1", "B2B", "acme.com", "hi@acme.com");

        // Act
        await sut.UpdateAsync(customer.Id.Value, input, CancellationToken.None);

        // Assert
        customer.Profile.CompanyName.Should().Be("Acme Ltd");
        await repo.Received(1).UpdateAsync(customer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_WhenCustomerMissing_DoesNothing()
    {
        // Arrange
        var repo = Substitute.For<ICustomerRepository>();
        repo.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        var sut = new UpdateCompanyProfile(repo);

        // Act
        var act = async () => await sut.UpdateAsync(Guid.NewGuid(), new CompanyProfileInput(null, null, null, null, null, null, null), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_MapsProfileToView_AndReportsEmpty()
    {
        // Arrange
        var customer = Customer.Create("sam@acme.com");
        var repo = Substitute.For<ICustomerRepository>();
        repo.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new GetCompanyProfile(repo);

        // Act
        var view = await sut.ForCustomerAsync(customer.Id.Value, CancellationToken.None);

        // Assert
        view.IsEmpty.Should().BeTrue();
        view.CompanyName.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Application.Tests --filter CompanyProfileUseCasesTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement records, use cases, DI**

`CompanyProfileInput.cs`:

```csharp
namespace Coldstart.Application.Customers;

public sealed record CompanyProfileInput(
    string? CompanyName, string? Address, string? VatNumber, string? KvkNumber,
    string? Niche, string? Website, string? ContactEmail);
```

`CompanyProfileView.cs`:

```csharp
namespace Coldstart.Application.Customers;

public sealed record CompanyProfileView(
    string? CompanyName, string? Address, string? VatNumber, string? KvkNumber,
    string? Niche, string? Website, string? ContactEmail)
{
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(CompanyName) && string.IsNullOrWhiteSpace(Address)
        && string.IsNullOrWhiteSpace(VatNumber) && string.IsNullOrWhiteSpace(KvkNumber)
        && string.IsNullOrWhiteSpace(Niche) && string.IsNullOrWhiteSpace(Website)
        && string.IsNullOrWhiteSpace(ContactEmail);
}
```

`IUpdateCompanyProfile.cs`:

```csharp
namespace Coldstart.Application.Customers;

public interface IUpdateCompanyProfile
{
    Task UpdateAsync(Guid customerId, CompanyProfileInput input, CancellationToken ct);
}
```

`UpdateCompanyProfile.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

internal sealed class UpdateCompanyProfile(ICustomerRepository customers) : IUpdateCompanyProfile
{
    public async Task UpdateAsync(Guid customerId, CompanyProfileInput input, CancellationToken ct)
    {
        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is null) return;
        customer.UpdateProfile(new CompanyProfile(input.CompanyName, input.Address, input.VatNumber,
            input.KvkNumber, input.Niche, input.Website, input.ContactEmail));
        await customers.UpdateAsync(customer, ct);
    }
}
```

`IGetCompanyProfile.cs`:

```csharp
namespace Coldstart.Application.Customers;

public interface IGetCompanyProfile
{
    Task<CompanyProfileView> ForCustomerAsync(Guid customerId, CancellationToken ct);
}
```

`GetCompanyProfile.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

internal sealed class GetCompanyProfile(ICustomerRepository customers) : IGetCompanyProfile
{
    public async Task<CompanyProfileView> ForCustomerAsync(Guid customerId, CancellationToken ct)
    {
        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        var p = customer?.Profile ?? CompanyProfile.Empty;
        return new CompanyProfileView(p.CompanyName, p.Address, p.VatNumber, p.KvkNumber, p.Niche, p.Website, p.ContactEmail);
    }
}
```

In `DependencyInjection.cs` `AddApplication`:

```csharp
        services.AddScoped<Customers.IUpdateCompanyProfile, Customers.UpdateCompanyProfile>();
        services.AddScoped<Customers.IGetCompanyProfile, Customers.GetCompanyProfile>();
```

- [ ] **Step 4: Run to green, build, commit**

Run: `dotnet test tests/Coldstart.Application.Tests --filter CompanyProfileUseCasesTests` → PASS; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Application tests/Coldstart.Application.Tests/CompanyProfileUseCasesTests.cs
git commit -m "feat(profile): get/update company profile use cases + read model"
```

---

### Task 4: /app dashboard shell + profile summary + /app/profile edit page

**Files:**
- Modify: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml`, `Index.cshtml.cs`, `src/Coldstart.Web/wwwroot/css/site.css`
- Create: `src/Coldstart.Web/Areas/App/Pages/Profile.cshtml`, `Profile.cshtml.cs`
- Test: `tests/Coldstart.Web.Tests/CustomerZoneTests.cs` (add)

**Interfaces:**
- Consumes: `IGetCompanyProfile`, `IUpdateCompanyProfile`, `IGetLeadInbox`, `UserManager<ApplicationUser>`, `CompanyProfileView`/`CompanyProfileInput`.

- [ ] **Step 1: Write the failing zone/profile tests**

Add to `tests/Coldstart.Web.Tests/CustomerZoneTests.cs`:

```csharp
    [Fact]
    public async Task App_ShowsEmptyProfilePrompt_ForFreshCustomer()
    {
        // Arrange
        var client = factory.CreateClient();
        await RegisterAsync(client, $"prof-{Guid.NewGuid():N}@test.com", "Passw0rd!");

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("Add your company details");   // empty-profile prompt
        html.Should().Contain("app-bar");                     // dashboard shell
    }

    [Fact]
    public async Task ProfilePost_PersistsAndRendersOnZone()
    {
        // Arrange
        var client = factory.CreateClient();
        await RegisterAsync(client, $"prof2-{Guid.NewGuid():N}@test.com", "Passw0rd!");
        var form = await AntiforgeryForm(client, "/app/profile", new()
        {
            ["Input.CompanyName"] = "Acme Ltd", ["Input.VatNumber"] = "NL999",
            ["Input.Niche"] = "B2B SaaS", ["Input.Address"] = "", ["Input.KvkNumber"] = "",
            ["Input.Website"] = "", ["Input.ContactEmail"] = "",
        });

        // Act
        var post = await client.PostAsync("/app/profile", new FormUrlEncodedContent(form));
        var zoneHtml = await client.GetStringAsync("/app");

        // Assert
        post.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
        zoneHtml.Should().Contain("Acme Ltd");
        zoneHtml.Should().Contain("NL999");
    }
```

(Reuse/generalize the antiforgery-token helper from `ContactTests`/`AuthFlowTests` — a `AntiforgeryForm(client, path, fields)` that GETs `path`, scrapes `__RequestVerificationToken`, and merges it. Add the needed `using System.Net;`.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests --filter CustomerZoneTests`
Expected: FAIL — no `app-bar`/profile prompt / `/app/profile` 404.

- [ ] **Step 3: Update the zone PageModel + view**

`Areas/App/Pages/Index.cshtml.cs` — inject `IGetCompanyProfile` and expose the view:

```csharp
using Coldstart.Application.Customers;
using Coldstart.Application.Leads;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages;

[Authorize]
public sealed class IndexModel(UserManager<ApplicationUser> users, IGetLeadInbox inbox, IGetCompanyProfile profiles) : PageModel
{
    public string CustomerName { get; private set; } = "—";
    public IReadOnlyList<LeadSummary> Leads { get; private set; } = [];
    public CompanyProfileView Profile { get; private set; } = new(null, null, null, null, null, null, null);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return;
        var result = await inbox.ForCustomerAsync(user.CustomerId.Value, ct);
        CustomerName = result.CustomerName;
        Leads = result.Leads;
        Profile = await profiles.ForCustomerAsync(user.CustomerId.Value, ct);
    }
}
```

`Areas/App/Pages/Index.cshtml` — wrap in an app-bar + profile summary above the existing leads grid:

```html
@page "/app"
@model Coldstart.Web.Areas.App.Pages.IndexModel
@{ ViewData["Title"] = "Your leads"; Layout = "/Pages/Shared/_Layout.cshtml"; }

<header class="app-bar">
  <a class="brand" href="/"><img src="/logo.svg" alt="Coldstart" style="height:30px;width:auto;display:block;"></a>
  <nav class="app-nav">
    <a href="/app/profile">Company profile</a>
    <form method="post" asp-page="/Account/Logout" asp-area="Identity"><button class="btn btn-ghost" type="submit">Log out</button></form>
  </nav>
</header>

<main class="wrap app-zone">
  <section class="app-profile">
    @if (Model.Profile.IsEmpty)
    {
      <div class="app-profile-empty">
        <div><strong>Add your company details</strong><p>Address, VAT, KvK, niche — shown on your account.</p></div>
        <a class="btn btn-primary" href="/app/profile">Add details →</a>
      </div>
    }
    else
    {
      <div class="app-profile-card">
        <h2>@(Model.Profile.CompanyName ?? Model.CustomerName)</h2>
        <dl>
          @if (!string.IsNullOrWhiteSpace(Model.Profile.Address)) { <div><dt>Address</dt><dd>@Model.Profile.Address</dd></div> }
          @if (!string.IsNullOrWhiteSpace(Model.Profile.VatNumber)) { <div><dt>VAT</dt><dd>@Model.Profile.VatNumber</dd></div> }
          @if (!string.IsNullOrWhiteSpace(Model.Profile.KvkNumber)) { <div><dt>KvK</dt><dd>@Model.Profile.KvkNumber</dd></div> }
          @if (!string.IsNullOrWhiteSpace(Model.Profile.Niche)) { <div><dt>Niche</dt><dd>@Model.Profile.Niche</dd></div> }
          @if (!string.IsNullOrWhiteSpace(Model.Profile.Website)) { <div><dt>Website</dt><dd>@Model.Profile.Website</dd></div> }
          @if (!string.IsNullOrWhiteSpace(Model.Profile.ContactEmail)) { <div><dt>Contact</dt><dd>@Model.Profile.ContactEmail</dd></div> }
        </dl>
        <a class="app-edit-link" href="/app/profile">Edit</a>
      </div>
    }
  </section>

  <h1 class="app-leads-title">Leads @(Model.Leads.Count == 0 ? "" : $"({Model.Leads.Count})")</h1>
  @* --- existing Stage-4 leads grid / empty state markup stays here unchanged --- *@
  @if (Model.Leads.Count == 0)
  {
    <div class="app-empty"><h2>No leads yet</h2><p>Your next harvest will fill this in.</p></div>
  }
  else
  {
    <div class="app-leads">
      @foreach (var lead in Model.Leads)
      {
        <article class="app-lead">
          <div class="app-lead-top"><h3>@lead.Company</h3><span class="fit fit-@lead.Fit.ToLower()">@lead.Fit</span></div>
          <div class="app-lead-meta"><span class="chip">@lead.Source</span><span class="score">score @lead.Score.ToString("0.00")</span></div>
          <p class="app-lead-subj">@lead.Subject</p>
          <details class="app-lead-draft"><summary>Preview draft</summary><p>@lead.Draft</p></details>
          <div class="app-lead-foot"><span class="to">@(string.IsNullOrEmpty(lead.RecipientEmail) ? "—" : lead.RecipientEmail)</span><span class="conf conf-@lead.EmailConfidence.ToLower()">@lead.EmailConfidence</span><span class="status">@lead.Status</span></div>
        </article>
      }
    </div>
  }
</main>
```

(If the existing Stage-4 view already has the leads markup, keep it verbatim and only add the `app-bar` + `app-profile` section above it + drop the old inline header/logout since the app-bar now owns them.)

- [ ] **Step 4: Add the profile edit page**

`Areas/App/Pages/Profile.cshtml.cs`:

```csharp
using Coldstart.Application.Customers;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages;

[Authorize]
public sealed class ProfileModel(UserManager<ApplicationUser> users, IGetCompanyProfile getProfile, IUpdateCompanyProfile updateProfile) : PageModel
{
    [BindProperty] public ProfileInput Input { get; set; } = new();

    public sealed class ProfileInput
    {
        public string? CompanyName { get; set; }
        public string? Address { get; set; }
        public string? VatNumber { get; set; }
        public string? KvkNumber { get; set; }
        public string? Niche { get; set; }
        public string? Website { get; set; }
        public string? ContactEmail { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("Index");
        var v = await getProfile.ForCustomerAsync(user.CustomerId.Value, ct);
        Input = new ProfileInput { CompanyName = v.CompanyName, Address = v.Address, VatNumber = v.VatNumber, KvkNumber = v.KvkNumber, Niche = v.Niche, Website = v.Website, ContactEmail = v.ContactEmail };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("Index");
        await updateProfile.UpdateAsync(user.CustomerId.Value,
            new CompanyProfileInput(Input.CompanyName, Input.Address, Input.VatNumber, Input.KvkNumber, Input.Niche, Input.Website, Input.ContactEmail), ct);
        return RedirectToPage("Index");
    }
}
```

`Areas/App/Pages/Profile.cshtml`:

```html
@page "/app/profile"
@model Coldstart.Web.Areas.App.Pages.ProfileModel
@{ ViewData["Title"] = "Company profile"; Layout = "/Pages/Shared/_Layout.cshtml"; }

<main class="wrap auth-shell">
  <div class="auth-card">
    <h1>Company profile</h1>
    <p class="auth-sub">Shown on your account. All fields optional.</p>
    <form method="post">
      <div class="field"><label asp-for="Input.CompanyName">Company name</label><input asp-for="Input.CompanyName"></div>
      <div class="field"><label asp-for="Input.Address">Address</label><input asp-for="Input.Address"></div>
      <div class="field"><label asp-for="Input.VatNumber">VAT number</label><input asp-for="Input.VatNumber"></div>
      <div class="field"><label asp-for="Input.KvkNumber">KvK number</label><input asp-for="Input.KvkNumber"></div>
      <div class="field"><label asp-for="Input.Niche">Niche</label><input asp-for="Input.Niche"></div>
      <div class="field"><label asp-for="Input.Website">Website</label><input asp-for="Input.Website"></div>
      <div class="field"><label asp-for="Input.ContactEmail">Contact email</label><input asp-for="Input.ContactEmail" type="email"></div>
      <button class="btn btn-primary" type="submit">Save →</button>
    </form>
    <p class="auth-alt"><a href="/app">← Back to your leads</a></p>
  </div>
</main>
```

- [ ] **Step 5: Add app-bar + profile styles to site.css**

Append (reuse brand vars):

```css
  .app-bar { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding: 12px clamp(20px,5vw,56px); border-bottom: 2px solid var(--line-2); background: var(--cream-2); }
  .app-nav { display: flex; align-items: center; gap: 16px; }
  .app-nav a { font-family: var(--mono); font-size: 0.74rem; text-transform: uppercase; letter-spacing: 0.06em; color: var(--ink-soft); text-decoration: none; }
  .app-nav a:hover { color: var(--ink); }
  .app-profile { margin: 24px 0 8px; }
  .app-profile-empty { display: flex; align-items: center; justify-content: space-between; gap: 16px; background: var(--cream-2); border: 1.5px dashed var(--line-2); border-radius: 14px; padding: 20px 22px; }
  .app-profile-empty p { margin: 4px 0 0; color: var(--ink-soft); font-size: 0.9rem; }
  .app-profile-card { position: relative; background: var(--cream-2); border: 1.5px solid var(--line-2); border-radius: 14px; box-shadow: 0 4px 0 var(--line-2); padding: 20px 22px; }
  .app-profile-card h2 { margin: 0 0 12px; font-size: 1.2rem; }
  .app-profile-card dl { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 10px 24px; margin: 0; }
  .app-profile-card dt { font-family: var(--mono); font-size: 0.66rem; text-transform: uppercase; letter-spacing: 0.08em; color: var(--orange-d); }
  .app-profile-card dd { margin: 0; }
  .app-edit-link { position: absolute; top: 18px; right: 20px; font-family: var(--mono); font-size: 0.72rem; color: var(--orange-d); text-decoration: none; font-weight: 700; }
  .app-leads-title { font-size: 1.3rem; margin: 24px 0 14px; }
```

- [ ] **Step 6: Run to green, build, commit**

Run: `dotnet test tests/Coldstart.Web.Tests --filter CustomerZoneTests` → PASS; `dotnet test Coldstart.sln` → all green; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Web tests/Coldstart.Web.Tests
git commit -m "feat(zone): app-bar shell + company-profile summary + /app/profile edit page"
```

---

## Self-Review

**Spec coverage:**
- Landing "Log in" without scrolling → Task 1. ✓
- CompanyProfile value object + Customer.UpdateProfile + EF owned-type + migration → Task 2. ✓
- `ICustomerRepository.GetAsync`/`UpdateAsync` → Task 2. ✓
- Get/Update use cases + `CompanyProfileView`/`CompanyProfileInput` (Web entity-free) → Task 3. ✓
- Zone app-shell + profile summary (+ empty state) + `/app/profile` edit form (Authorize, antiforgery) → Task 4. ✓
- Testing: domain (UpdateProfile), app (use cases), infra (owned-type round-trip), web (empty prompt, POST persists, app-bar, landing hero login) → Tasks 1–4. ✓
- Non-goals (admin, invitations, disable register, auth hardening) untouched. ✓

**Placeholder scan:** none — full code/markup/CSS given. Task 4 Step 3 says keep the existing Stage-4 leads markup verbatim and add the app-bar/profile above it — concrete, not a placeholder.

**Type consistency:** `CompanyProfile(CompanyName,Address,VatNumber,KvkNumber,Niche,Website,ContactEmail)` + `Empty`; `Customer.Profile`/`UpdateProfile`; `ICustomerRepository.GetAsync(CustomerId,ct)`/`UpdateAsync(Customer,ct)`; `CompanyProfileInput`/`CompanyProfileView(IsEmpty)`; `IGetCompanyProfile.ForCustomerAsync(Guid,ct)`; `IUpdateCompanyProfile.UpdateAsync(Guid,CompanyProfileInput,ct)` — consistent across Tasks 2–4. The `/app/profile` form field names (`Input.CompanyName` …) match the test's posted keys.
```

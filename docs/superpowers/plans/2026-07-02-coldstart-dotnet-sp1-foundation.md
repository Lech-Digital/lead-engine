# Coldstart .NET SP1 — Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a hexagonal .NET 10 solution with a `Customer`/`Lead` domain, EF Core + Postgres persistence, and a Razor page that lists seeded leads — proving the whole stack end-to-end.

**Architecture:** Four projects with inward-only dependencies — `Coldstart.Domain` (zero deps), `Coldstart.Application` (ports + use cases), `Coldstart.Infrastructure` (EF Core/Postgres adapters), `Coldstart.Web` (Razor Pages + composition root). Persistence is reached only through Application ports; the Web layer sees read-model records, never domain entities.

**Tech Stack:** .NET 10, C#, EF Core 10 + Npgsql (Postgres 17 via Docker), Razor Pages, xUnit + NSubstitute + FluentAssertions, Testcontainers.PostgreSql, ASP.NET Core Mvc.Testing.

## Global Constraints

- **TFM:** `net10.0` for every project.
- **Hexagonal (CS-HEX-01/02/03):** Domain has zero external package references. Ports (interfaces) live in Application, not Infrastructure. DI wiring only in the Web composition root.
- **DDD (CS-DDD-01/02):** Value objects are `record` types; entities use `private set`/`init` + factory methods; behaviour via methods, no public setters; no anemic models.
- **Data (CS-DATA-01/03):** Repositories return `Task<IReadOnlyList<T>>` — never `IQueryable`. Async all the way; `CancellationToken` on every IO method.
- **Modifiers (CS-MOD-01):** `internal` for implementation classes; `[InternalsVisibleTo]` for test projects only.
- **Testing:** xUnit + NSubstitute + FluentAssertions; naming `Method_State_Expected`; strict AAA with `// Arrange`/`// Act`/`// Assert`; only mock outbound ports, instantiate domain objects directly; ≥1 happy + 2 sad paths per class.
- **FluentAssertions pinned to `7.*`** (last MIT-licensed release; v8+ requires a commercial license). Copy this version exactly in every test project.
- **Repo layout:** .NET at repo root (`Coldstart.sln`, `src/`, `tests/`). Python stays under `leadengine/` etc. for clean later removal.
- **Secrets:** connection strings via `appsettings.Development.json` (gitignored) / user-secrets / env — never committed.

---

## File Structure

```
/  (repo root)
  Coldstart.sln
  docker-compose.yml
  .gitignore                       (append .NET + appsettings.Development.json)
  src/
    Coldstart.Domain/
      Customer.cs  Lead.cs
      CustomerId.cs  LeadId.cs  EmailAddress.cs
      Fit.cs  LeadStatus.cs  EmailConfidence.cs
      DomainException.cs
    Coldstart.Application/
      Ports/ICustomerRepository.cs  Ports/ILeadRepository.cs
      Leads/IListLeads.cs  Leads/ListLeads.cs  Leads/LeadSummary.cs
      DependencyInjection.cs
    Coldstart.Infrastructure/
      ColdstartDbContext.cs
      Configurations/CustomerConfiguration.cs  Configurations/LeadConfiguration.cs
      Repositories/CustomerRepository.cs  Repositories/LeadRepository.cs
      DesignTimeDbContextFactory.cs
      DbSeeder.cs
      DependencyInjection.cs
      Migrations/                  (generated)
    Coldstart.Web/
      Program.cs
      Pages/Leads.cshtml  Pages/Leads.cshtml.cs
      appsettings.json
  tests/
    Coldstart.Domain.Tests/
    Coldstart.Application.Tests/
    Coldstart.Infrastructure.Tests/
    Coldstart.Web.Tests/
```

---

### Task 1: Solution skeleton, projects, references, Postgres compose

**Files:**
- Create: `Coldstart.sln`, `src/Coldstart.{Domain,Application,Infrastructure,Web}/`, `tests/Coldstart.{Domain,Application,Infrastructure,Web}.Tests/`, `docker-compose.yml`
- Modify: `.gitignore`
- Test: `tests/Coldstart.Domain.Tests/SmokeTest.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: the solution + project graph every later task builds on. Namespaces: `Coldstart.Domain`, `Coldstart.Application`, `Coldstart.Infrastructure`, `Coldstart.Web`.

- [ ] **Step 1: Create the solution and projects**

```bash
cd /Users/andrzej.lech/Code/private/lead-engine
dotnet new sln -n Coldstart
dotnet new classlib -n Coldstart.Domain        -o src/Coldstart.Domain        -f net10.0
dotnet new classlib -n Coldstart.Application   -o src/Coldstart.Application   -f net10.0
dotnet new classlib -n Coldstart.Infrastructure -o src/Coldstart.Infrastructure -f net10.0
dotnet new webapp   -n Coldstart.Web           -o src/Coldstart.Web           -f net10.0
dotnet new xunit    -n Coldstart.Domain.Tests         -o tests/Coldstart.Domain.Tests         -f net10.0
dotnet new xunit    -n Coldstart.Application.Tests    -o tests/Coldstart.Application.Tests    -f net10.0
dotnet new xunit    -n Coldstart.Infrastructure.Tests -o tests/Coldstart.Infrastructure.Tests -f net10.0
dotnet new xunit    -n Coldstart.Web.Tests            -o tests/Coldstart.Web.Tests            -f net10.0
rm src/Coldstart.Domain/Class1.cs src/Coldstart.Application/Class1.cs src/Coldstart.Infrastructure/Class1.cs
```

- [ ] **Step 2: Add every project to the solution**

```bash
dotnet sln add src/Coldstart.Domain src/Coldstart.Application src/Coldstart.Infrastructure src/Coldstart.Web
dotnet sln add tests/Coldstart.Domain.Tests tests/Coldstart.Application.Tests tests/Coldstart.Infrastructure.Tests tests/Coldstart.Web.Tests
```

- [ ] **Step 3: Wire project references (inward-only)**

```bash
# Application -> Domain
dotnet add src/Coldstart.Application reference src/Coldstart.Domain
# Infrastructure -> Application (+ Domain transitively)
dotnet add src/Coldstart.Infrastructure reference src/Coldstart.Application
# Web -> Application + Infrastructure (composition root)
dotnet add src/Coldstart.Web reference src/Coldstart.Application src/Coldstart.Infrastructure
# Test refs
dotnet add tests/Coldstart.Domain.Tests reference src/Coldstart.Domain
dotnet add tests/Coldstart.Application.Tests reference src/Coldstart.Application
dotnet add tests/Coldstart.Infrastructure.Tests reference src/Coldstart.Infrastructure
dotnet add tests/Coldstart.Web.Tests reference src/Coldstart.Web
```

- [ ] **Step 4: Add test-only packages (pin FluentAssertions to 7.x)**

```bash
for p in Coldstart.Domain.Tests Coldstart.Application.Tests Coldstart.Infrastructure.Tests Coldstart.Web.Tests; do
  dotnet add tests/$p package FluentAssertions --version 7.*
  dotnet add tests/$p package NSubstitute
done
```

- [ ] **Step 5: Create `docker-compose.yml` for local Postgres**

```yaml
services:
  db:
    image: postgres:17
    environment:
      POSTGRES_DB: coldstart
      POSTGRES_USER: coldstart
      POSTGRES_PASSWORD: localdev
    ports:
      - "5432:5432"
    volumes:
      - coldstart-pgdata:/var/lib/postgresql/data
volumes:
  coldstart-pgdata:
```

- [ ] **Step 6: Append .NET entries to `.gitignore`**

```gitignore
# .NET
bin/
obj/
src/Coldstart.Web/appsettings.Development.json
```

- [ ] **Step 7: Replace the default test with a smoke test**

Delete `tests/Coldstart.Domain.Tests/UnitTest1.cs`, create `tests/Coldstart.Domain.Tests/SmokeTest.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class SmokeTest
{
    [Fact]
    public void Solution_Compiles_AndTestsRun()
    {
        // Arrange / Act
        var truth = true;

        // Assert
        truth.Should().BeTrue();
    }
}
```

- [ ] **Step 8: Build and test**

Run: `dotnet build && dotnet test`
Expected: build succeeds; `Passed! - Failed: 0` (1 test).

- [ ] **Step 9: Commit**

```bash
git add Coldstart.sln src/ tests/ docker-compose.yml .gitignore
git commit -m "feat(dotnet): scaffold hexagonal solution skeleton + Postgres compose"
```

---

### Task 2: Domain value objects, enums, and DomainException

**Files:**
- Create: `src/Coldstart.Domain/CustomerId.cs`, `LeadId.cs`, `EmailAddress.cs`, `Fit.cs`, `LeadStatus.cs`, `EmailConfidence.cs`, `DomainException.cs`
- Test: `tests/Coldstart.Domain.Tests/EmailAddressTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `record CustomerId(Guid Value)` with `static CustomerId New()`
  - `record LeadId(Guid Value)` with `static LeadId New()`
  - `record EmailAddress` — ctor `EmailAddress(string value)`, property `string Value`, throws `DomainException` on invalid
  - `enum Fit { Yes, Maybe, No }`
  - `enum LeadStatus { PendingReview, Approved, Rejected, Sent, Shelved }`
  - `enum EmailConfidence { None, Inferred, CatchAll, Verified }`
  - `class DomainException : Exception`

- [ ] **Step 1: Write the failing test**

`tests/Coldstart.Domain.Tests/EmailAddressTests.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class EmailAddressTests
{
    [Fact]
    public void Ctor_WithValidAddress_NormalizesToLowerTrimmed()
    {
        // Arrange / Act
        var email = new EmailAddress("  Anand@Patronus.AI ");

        // Assert
        email.Value.Should().Be("anand@patronus.ai");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    public void Ctor_WithInvalidAddress_ThrowsDomainException(string bad)
    {
        // Arrange
        var act = () => new EmailAddress(bad);

        // Act / Assert
        act.Should().Throw<DomainException>();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Domain.Tests`
Expected: FAIL — `EmailAddress`/`DomainException` do not exist.

- [ ] **Step 3: Implement the domain primitives**

`src/Coldstart.Domain/DomainException.cs`:

```csharp
namespace Coldstart.Domain;

public sealed class DomainException(string message) : Exception(message);
```

`src/Coldstart.Domain/CustomerId.cs`:

```csharp
namespace Coldstart.Domain;

public sealed record CustomerId(Guid Value)
{
    public static CustomerId New() => new(Guid.NewGuid());
}
```

`src/Coldstart.Domain/LeadId.cs`:

```csharp
namespace Coldstart.Domain;

public sealed record LeadId(Guid Value)
{
    public static LeadId New() => new(Guid.NewGuid());
}
```

`src/Coldstart.Domain/EmailAddress.cs`:

```csharp
namespace Coldstart.Domain;

public sealed record EmailAddress
{
    public string Value { get; }

    public EmailAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('@'))
            throw new DomainException($"Invalid email address: '{value}'.");
        Value = value.Trim().ToLowerInvariant();
    }

    public override string ToString() => Value;
}
```

`src/Coldstart.Domain/Fit.cs`:

```csharp
namespace Coldstart.Domain;

public enum Fit { Yes, Maybe, No }
```

`src/Coldstart.Domain/LeadStatus.cs`:

```csharp
namespace Coldstart.Domain;

public enum LeadStatus { PendingReview, Approved, Rejected, Sent, Shelved }
```

`src/Coldstart.Domain/EmailConfidence.cs`:

```csharp
namespace Coldstart.Domain;

public enum EmailConfidence { None, Inferred, CatchAll, Verified }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Coldstart.Domain.Tests`
Expected: PASS (smoke + 4 email cases).

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Domain tests/Coldstart.Domain.Tests/EmailAddressTests.cs
git commit -m "feat(domain): add value objects, enums, DomainException"
```

---

### Task 3: Lead aggregate

**Files:**
- Create: `src/Coldstart.Domain/Lead.cs`
- Test: `tests/Coldstart.Domain.Tests/LeadTests.cs`

**Interfaces:**
- Consumes: `CustomerId`, `Fit`, `LeadStatus`, `EmailAddress`, `EmailConfidence`, `DomainException` (Task 2).
- Produces: `Lead` aggregate:
  - `static Lead Create(CustomerId customerId, string company, Fit fit, string subject, string draft, DateTimeOffset createdAt)`
  - props (all `private set`): `LeadId Id`, `CustomerId CustomerId`, `string Company`, `Fit Fit`, `string Subject`, `string Draft`, `LeadStatus Status`, `EmailAddress? RecipientEmail`, `EmailConfidence EmailConfidence`, `DateTimeOffset CreatedAt`, `DateTimeOffset? SentAt`
  - `void Approve(EmailAddress recipient)`, `void Reject()`, `void Shelve()`, `void MarkSent(DateTimeOffset when)`

- [ ] **Step 1: Write the failing test**

`tests/Coldstart.Domain.Tests/LeadTests.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class LeadTests
{
    private static Lead NewLead() =>
        Lead.Create(CustomerId.New(), "Patronus AI", Fit.Yes, "Subject", "Body",
            new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Create_SetsPendingReview_AndNoEmailConfidence()
    {
        // Act
        var lead = NewLead();

        // Assert
        lead.Status.Should().Be(LeadStatus.PendingReview);
        lead.EmailConfidence.Should().Be(EmailConfidence.None);
        lead.RecipientEmail.Should().BeNull();
    }

    [Fact]
    public void Approve_WithRecipient_SetsApprovedAndRecipient()
    {
        // Arrange
        var lead = NewLead();

        // Act
        lead.Approve(new EmailAddress("anand@patronus.ai"));

        // Assert
        lead.Status.Should().Be(LeadStatus.Approved);
        lead.RecipientEmail!.Value.Should().Be("anand@patronus.ai");
    }

    [Fact]
    public void MarkSent_WhenNotApproved_Throws()
    {
        // Arrange
        var lead = NewLead();

        // Act
        var act = () => lead.MarkSent(DateTimeOffset.UtcNow);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Approve_WhenAlreadySent_Throws()
    {
        // Arrange
        var lead = NewLead();
        lead.Approve(new EmailAddress("anand@patronus.ai"));
        lead.MarkSent(DateTimeOffset.UtcNow);

        // Act
        var act = () => lead.Approve(new EmailAddress("other@patronus.ai"));

        // Assert
        act.Should().Throw<DomainException>();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Domain.Tests`
Expected: FAIL — `Lead` does not exist.

- [ ] **Step 3: Implement `Lead`**

`src/Coldstart.Domain/Lead.cs`:

```csharp
namespace Coldstart.Domain;

public sealed class Lead
{
    public LeadId Id { get; private set; } = null!;
    public CustomerId CustomerId { get; private set; } = null!;
    public string Company { get; private set; } = string.Empty;
    public Fit Fit { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string Draft { get; private set; } = string.Empty;
    public LeadStatus Status { get; private set; }
    public EmailAddress? RecipientEmail { get; private set; }
    public EmailConfidence EmailConfidence { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    private Lead() { } // EF materialization

    private Lead(LeadId id, CustomerId customerId, string company, Fit fit,
        string subject, string draft, DateTimeOffset createdAt)
    {
        Id = id;
        CustomerId = customerId;
        Company = company;
        Fit = fit;
        Subject = subject;
        Draft = draft;
        CreatedAt = createdAt;
        Status = LeadStatus.PendingReview;
        EmailConfidence = EmailConfidence.None;
    }

    public static Lead Create(CustomerId customerId, string company, Fit fit,
        string subject, string draft, DateTimeOffset createdAt) =>
        new(LeadId.New(), customerId, company, fit, subject, draft, createdAt);

    public void Approve(EmailAddress recipient)
    {
        if (Status == LeadStatus.Sent)
            throw new DomainException("Cannot approve an already-sent lead.");
        RecipientEmail = recipient ?? throw new DomainException("Approval requires a recipient email.");
        Status = LeadStatus.Approved;
    }

    public void Reject() => Status = LeadStatus.Rejected;

    public void Shelve() => Status = LeadStatus.Shelved;

    public void MarkSent(DateTimeOffset when)
    {
        if (Status != LeadStatus.Approved || RecipientEmail is null)
            throw new DomainException("Only an approved lead with a recipient can be marked sent.");
        Status = LeadStatus.Sent;
        SentAt = when;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Coldstart.Domain.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Domain/Lead.cs tests/Coldstart.Domain.Tests/LeadTests.cs
git commit -m "feat(domain): add Lead aggregate with status transitions"
```

---

### Task 4: Customer aggregate

**Files:**
- Create: `src/Coldstart.Domain/Customer.cs`
- Test: `tests/Coldstart.Domain.Tests/CustomerTests.cs`

**Interfaces:**
- Consumes: `CustomerId`, `Lead`, `Fit` (Tasks 2–3).
- Produces: `Customer` aggregate:
  - `static Customer Create(string name)`
  - props: `CustomerId Id`, `string Name`, `IReadOnlyList<Lead> Leads`
  - `Lead AddLead(string company, Fit fit, string subject, string draft, DateTimeOffset createdAt)`

- [ ] **Step 1: Write the failing test**

`tests/Coldstart.Domain.Tests/CustomerTests.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class CustomerTests
{
    [Fact]
    public void Create_SetsNameAndEmptyLeads()
    {
        // Act
        var customer = Customer.Create("Coldstart");

        // Assert
        customer.Name.Should().Be("Coldstart");
        customer.Leads.Should().BeEmpty();
    }

    [Fact]
    public void AddLead_AttachesLeadToCustomer()
    {
        // Arrange
        var customer = Customer.Create("Coldstart");

        // Act
        var lead = customer.AddLead("Patronus AI", Fit.Yes, "Subj", "Body", DateTimeOffset.UtcNow);

        // Assert
        customer.Leads.Should().ContainSingle().Which.Should().BeSameAs(lead);
        lead.CustomerId.Should().Be(customer.Id);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Domain.Tests`
Expected: FAIL — `Customer` does not exist.

- [ ] **Step 3: Implement `Customer`**

`src/Coldstart.Domain/Customer.cs`:

```csharp
namespace Coldstart.Domain;

public sealed class Customer
{
    private readonly List<Lead> _leads = new();

    public CustomerId Id { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public IReadOnlyList<Lead> Leads => _leads.AsReadOnly();

    private Customer() { } // EF materialization

    private Customer(CustomerId id, string name)
    {
        Id = id;
        Name = name;
    }

    public static Customer Create(string name) => new(CustomerId.New(), name);

    public Lead AddLead(string company, Fit fit, string subject, string draft, DateTimeOffset createdAt)
    {
        var lead = Lead.Create(Id, company, fit, subject, draft, createdAt);
        _leads.Add(lead);
        return lead;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Coldstart.Domain.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Domain/Customer.cs tests/Coldstart.Domain.Tests/CustomerTests.cs
git commit -m "feat(domain): add Customer aggregate"
```

---

### Task 5: Application ports, LeadSummary read model, ListLeads use case

**Files:**
- Create: `src/Coldstart.Application/Ports/ICustomerRepository.cs`, `Ports/ILeadRepository.cs`, `Leads/LeadSummary.cs`, `Leads/IListLeads.cs`, `Leads/ListLeads.cs`, `DependencyInjection.cs`
- Modify: `src/Coldstart.Application/Coldstart.Application.csproj` (add `Microsoft.Extensions.DependencyInjection.Abstractions`)
- Test: `tests/Coldstart.Application.Tests/ListLeadsTests.cs`

**Interfaces:**
- Consumes: `Customer`, `Lead`, `CustomerId`, `Fit`, `LeadStatus`, `EmailConfidence`.
- Produces:
  - `interface ILeadRepository { Task<IReadOnlyList<Lead>> ForCustomerAsync(CustomerId customerId, CancellationToken ct); Task AddAsync(Lead lead, CancellationToken ct); }`
  - `interface ICustomerRepository { Task<IReadOnlyList<Customer>> AllAsync(CancellationToken ct); Task AddAsync(Customer customer, CancellationToken ct); }`
  - `record LeadSummary(Guid Id, string Company, string Fit, string Status, string? RecipientEmail, string EmailConfidence)`
  - `interface IListLeads { Task<IReadOnlyList<LeadSummary>> ForCustomerAsync(CustomerId customerId, CancellationToken ct); }`
  - `ListLeads` implementing `IListLeads`
  - `static IServiceCollection AddApplication(this IServiceCollection services)`

- [ ] **Step 1: Add the DI abstractions package**

```bash
dotnet add src/Coldstart.Application package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet add tests/Coldstart.Application.Tests package Microsoft.Extensions.DependencyInjection.Abstractions
```

- [ ] **Step 2: Write the failing test**

`tests/Coldstart.Application.Tests/ListLeadsTests.cs`:

```csharp
using Coldstart.Application.Leads;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class ListLeadsTests
{
    [Fact]
    public async Task ForCustomerAsync_MapsDomainLeadsToSummaries()
    {
        // Arrange
        var customerId = CustomerId.New();
        var lead = Lead.Create(customerId, "Patronus AI", Fit.Yes, "Subj", "Body", DateTimeOffset.UtcNow);
        lead.Approve(new EmailAddress("anand@patronus.ai"));
        var repo = Substitute.For<ILeadRepository>();
        repo.ForCustomerAsync(customerId, Arg.Any<CancellationToken>())
            .Returns(new[] { lead });
        var sut = new ListLeads(repo);

        // Act
        var result = await sut.ForCustomerAsync(customerId, CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].Company.Should().Be("Patronus AI");
        result[0].Status.Should().Be("Approved");
        result[0].RecipientEmail.Should().Be("anand@patronus.ai");
    }

    [Fact]
    public async Task ForCustomerAsync_WhenNoLeads_ReturnsEmpty()
    {
        // Arrange
        var customerId = CustomerId.New();
        var repo = Substitute.For<ILeadRepository>();
        repo.ForCustomerAsync(customerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Lead>());
        var sut = new ListLeads(repo);

        // Act
        var result = await sut.ForCustomerAsync(customerId, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Application.Tests`
Expected: FAIL — types do not exist.

- [ ] **Step 4: Implement ports, read model, use case, DI**

`src/Coldstart.Application/Ports/ILeadRepository.cs`:

```csharp
using Coldstart.Domain;

namespace Coldstart.Application.Ports;

public interface ILeadRepository
{
    Task<IReadOnlyList<Lead>> ForCustomerAsync(CustomerId customerId, CancellationToken ct);
    Task AddAsync(Lead lead, CancellationToken ct);
}
```

`src/Coldstart.Application/Ports/ICustomerRepository.cs`:

```csharp
using Coldstart.Domain;

namespace Coldstart.Application.Ports;

public interface ICustomerRepository
{
    Task<IReadOnlyList<Customer>> AllAsync(CancellationToken ct);
    Task AddAsync(Customer customer, CancellationToken ct);
}
```

`src/Coldstart.Application/Leads/LeadSummary.cs`:

```csharp
namespace Coldstart.Application.Leads;

public sealed record LeadSummary(
    Guid Id,
    string Company,
    string Fit,
    string Status,
    string? RecipientEmail,
    string EmailConfidence);
```

`src/Coldstart.Application/Leads/IListLeads.cs`:

```csharp
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

public interface IListLeads
{
    Task<IReadOnlyList<LeadSummary>> ForCustomerAsync(CustomerId customerId, CancellationToken ct);
}
```

`src/Coldstart.Application/Leads/ListLeads.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

internal sealed class ListLeads(ILeadRepository leads) : IListLeads
{
    public async Task<IReadOnlyList<LeadSummary>> ForCustomerAsync(CustomerId customerId, CancellationToken ct)
    {
        var found = await leads.ForCustomerAsync(customerId, ct);
        return found
            .Select(l => new LeadSummary(
                l.Id.Value,
                l.Company,
                l.Fit.ToString(),
                l.Status.ToString(),
                l.RecipientEmail?.Value,
                l.EmailConfidence.ToString()))
            .ToList();
    }
}
```

Note: `ListLeads` is `internal`; expose it to the test project. Create `src/Coldstart.Application/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Coldstart.Application.Tests")]
```

`src/Coldstart.Application/DependencyInjection.cs`:

```csharp
using Coldstart.Application.Leads;
using Microsoft.Extensions.DependencyInjection;

namespace Coldstart.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IListLeads, ListLeads>();
        return services;
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/Coldstart.Application.Tests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Application tests/Coldstart.Application.Tests/ListLeadsTests.cs
git commit -m "feat(application): add repository ports, LeadSummary, ListLeads use case"
```

---

### Task 6: EF Core DbContext, mappings, repositories, integration test

**Files:**
- Create: `src/Coldstart.Infrastructure/ColdstartDbContext.cs`, `Configurations/CustomerConfiguration.cs`, `Configurations/LeadConfiguration.cs`, `Repositories/CustomerRepository.cs`, `Repositories/LeadRepository.cs`, `DesignTimeDbContextFactory.cs`, `DependencyInjection.cs`
- Test: `tests/Coldstart.Infrastructure.Tests/LeadRepositoryTests.cs`, `tests/Coldstart.Infrastructure.Tests/PostgresFixture.cs`

**Interfaces:**
- Consumes: `ILeadRepository`, `ICustomerRepository` (Task 5), domain types.
- Produces:
  - `ColdstartDbContext : DbContext` with `DbSet<Customer> Customers`, `DbSet<Lead> Leads`
  - `CustomerRepository`, `LeadRepository` implementing the ports
  - `static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)`

- [ ] **Step 1: Add EF Core + Testcontainers packages**

```bash
dotnet add src/Coldstart.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Coldstart.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet add src/Coldstart.Infrastructure package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet add tests/Coldstart.Infrastructure.Tests package Testcontainers.PostgreSql
dotnet add tests/Coldstart.Infrastructure.Tests package Microsoft.EntityFrameworkCore.Design
```

- [ ] **Step 2: Write the failing integration test**

`tests/Coldstart.Infrastructure.Tests/PostgresFixture.cs`:

```csharp
using Coldstart.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder().WithImage("postgres:17").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = NewDbContext();
        await db.Database.EnsureCreatedAsync();
    }

    public ColdstartDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<ColdstartDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new ColdstartDbContext(options);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
```

`tests/Coldstart.Infrastructure.Tests/LeadRepositoryTests.cs`:

```csharp
using Coldstart.Domain;
using Coldstart.Infrastructure.Repositories;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class LeadRepositoryTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task AddAndReadBack_RoundTripsValueObjectsAndEnums()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        var customer = Customer.Create("Coldstart");
        var lead = customer.AddLead("Patronus AI", Fit.Yes, "Subj", "Body", DateTimeOffset.UtcNow);
        lead.Approve(new EmailAddress("anand@patronus.ai"));
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        // Act
        await using var readDb = fixture.NewDbContext();
        var repo = new LeadRepository(readDb);
        var result = await repo.ForCustomerAsync(customer.Id, CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].Company.Should().Be("Patronus AI");
        result[0].Status.Should().Be(LeadStatus.Approved);
        result[0].RecipientEmail!.Value.Should().Be("anand@patronus.ai");
        result[0].EmailConfidence.Should().Be(EmailConfidence.None);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests`
Expected: FAIL — `ColdstartDbContext`/`LeadRepository` do not exist. (Docker must be running for Testcontainers.)

- [ ] **Step 4: Implement DbContext, configurations, repositories, DI, design-time factory**

`src/Coldstart.Infrastructure/ColdstartDbContext.cs`:

```csharp
using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Infrastructure;

public sealed class ColdstartDbContext(DbContextOptions<ColdstartDbContext> options)
    : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Lead> Leads => Set<Lead>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ColdstartDbContext).Assembly);
    }
}
```

`src/Coldstart.Infrastructure/Configurations/CustomerConfiguration.cs`:

```csharp
using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coldstart.Infrastructure.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new CustomerId(value))
            .ValueGeneratedNever();
        builder.Property(c => c.Name).IsRequired();
        builder.HasMany(c => c.Leads)
            .WithOne()
            .HasForeignKey(l => l.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Customer.Leads))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
```

`src/Coldstart.Infrastructure/Configurations/LeadConfiguration.cs`:

```csharp
using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coldstart.Infrastructure.Configurations;

internal sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        builder.ToTable("leads");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasConversion(id => id.Value, value => new LeadId(value))
            .ValueGeneratedNever();
        builder.Property(l => l.CustomerId)
            .HasConversion(id => id.Value, value => new CustomerId(value));
        builder.Property(l => l.Company).IsRequired();
        builder.Property(l => l.Subject).IsRequired();
        builder.Property(l => l.Draft).IsRequired();
        builder.Property(l => l.Fit).HasConversion<string>();
        builder.Property(l => l.Status).HasConversion<string>();
        builder.Property(l => l.EmailConfidence).HasConversion<string>();
        builder.Property(l => l.RecipientEmail)
            .HasConversion(
                email => email!.Value,
                value => new EmailAddress(value));
        builder.Property(l => l.CreatedAt);
        builder.Property(l => l.SentAt);
    }
}
```

`src/Coldstart.Infrastructure/Repositories/LeadRepository.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Infrastructure.Repositories;

internal sealed class LeadRepository(ColdstartDbContext db) : ILeadRepository
{
    public async Task<IReadOnlyList<Lead>> ForCustomerAsync(CustomerId customerId, CancellationToken ct) =>
        await db.Leads
            .Where(l => l.CustomerId == customerId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(Lead lead, CancellationToken ct)
    {
        await db.Leads.AddAsync(lead, ct);
        await db.SaveChangesAsync(ct);
    }
}
```

Note: `LeadRepository` is tested directly (constructed with a real DbContext), so it must be reachable from the test project. Make it `public` OR add `[InternalsVisibleTo]`. Use `[InternalsVisibleTo]` to honour CS-MOD-01 — create `src/Coldstart.Infrastructure/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Coldstart.Infrastructure.Tests")]
```

`src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Infrastructure.Repositories;

internal sealed class CustomerRepository(ColdstartDbContext db) : ICustomerRepository
{
    public async Task<IReadOnlyList<Customer>> AllAsync(CancellationToken ct) =>
        await db.Customers.OrderBy(c => c.Name).ToListAsync(ct);

    public async Task AddAsync(Customer customer, CancellationToken ct)
    {
        await db.Customers.AddAsync(customer, ct);
        await db.SaveChangesAsync(ct);
    }
}
```

`src/Coldstart.Infrastructure/DesignTimeDbContextFactory.cs` (lets `dotnet ef` build the context without the Web app):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Coldstart.Infrastructure;

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ColdstartDbContext>
{
    public ColdstartDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ColdstartDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=coldstart;Username=coldstart;Password=localdev")
            .Options;
        return new ColdstartDbContext(options);
    }
}
```

`src/Coldstart.Infrastructure/DependencyInjection.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coldstart.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ColdstartDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        return services;
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `docker compose up -d db && dotnet test tests/Coldstart.Infrastructure.Tests`
Expected: PASS — round-trip test green (Testcontainers spins its own Postgres; the compose DB is for the app).

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Infrastructure tests/Coldstart.Infrastructure.Tests
git commit -m "feat(infra): EF Core DbContext, mappings, repositories + integration test"
```

---

### Task 7: Initial migration + DbSeeder

**Files:**
- Create: `src/Coldstart.Infrastructure/DbSeeder.cs`, `src/Coldstart.Infrastructure/Migrations/` (generated)
- Test: `tests/Coldstart.Infrastructure.Tests/DbSeederTests.cs`

**Interfaces:**
- Consumes: `ColdstartDbContext`, `Customer` (Tasks 4, 6).
- Produces: `static class DbSeeder { static Task SeedAsync(ColdstartDbContext db, CancellationToken ct) }` — idempotent (no-op if a customer already exists).

- [ ] **Step 1: Install EF tools and create the initial migration**

```bash
dotnet tool install --global dotnet-ef 2>/dev/null || dotnet tool update --global dotnet-ef
dotnet ef migrations add InitialCreate \
  -p src/Coldstart.Infrastructure -s src/Coldstart.Infrastructure -o Migrations
```

- [ ] **Step 2: Write the failing test**

`tests/Coldstart.Infrastructure.Tests/DbSeederTests.cs`:

```csharp
using Coldstart.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class DbSeederTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task SeedAsync_OnEmptyDb_CreatesDemoCustomerWithLeads()
    {
        // Arrange
        await using var db = fixture.NewDbContext();

        // Act
        await DbSeeder.SeedAsync(db, CancellationToken.None);

        // Assert
        var customers = await db.Customers.Include(c => c.Leads).ToListAsync();
        customers.Should().ContainSingle();
        customers[0].Leads.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SeedAsync_WhenAlreadySeeded_IsIdempotent()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        await DbSeeder.SeedAsync(db, CancellationToken.None);

        // Act
        await DbSeeder.SeedAsync(db, CancellationToken.None);

        // Assert
        (await db.Customers.CountAsync()).Should().Be(1);
    }
}
```

Note: these tests share `PostgresFixture` (Task 6) which uses `EnsureCreated`; that is sufficient for seeding assertions.

- [ ] **Step 3: Run to verify it fails**

Run: `docker compose up -d db && dotnet test tests/Coldstart.Infrastructure.Tests --filter DbSeederTests`
Expected: FAIL — `DbSeeder` does not exist.

- [ ] **Step 4: Implement `DbSeeder`**

`src/Coldstart.Infrastructure/DbSeeder.cs`:

```csharp
using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Infrastructure;

public static class DbSeeder
{
    public static async Task SeedAsync(ColdstartDbContext db, CancellationToken ct)
    {
        if (await db.Customers.AnyAsync(ct)) return;

        var customer = Customer.Create("Coldstart");
        var now = DateTimeOffset.UtcNow;
        var a = customer.AddLead("Patronus AI", Fit.Yes, "Anand — a thought after the $50M", "…", now);
        a.Approve(new EmailAddress("anand@patronus.ai"));
        customer.AddLead("Lago", Fit.Yes, "hiring GTM at Lago", "…", now);
        customer.AddLead("Visiblie", Fit.Maybe, "congrats on the €500K", "…", now);

        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter DbSeederTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Infrastructure tests/Coldstart.Infrastructure.Tests/DbSeederTests.cs
git commit -m "feat(infra): initial EF migration + idempotent DbSeeder"
```

---

### Task 8: Web composition root + Leads Razor page + smoke test

**Files:**
- Modify: `src/Coldstart.Web/Program.cs`, `src/Coldstart.Web/appsettings.json`
- Create: `src/Coldstart.Web/Pages/Leads.cshtml`, `src/Coldstart.Web/Pages/Leads.cshtml.cs`, `src/Coldstart.Web/appsettings.Development.json` (gitignored)
- Test: `tests/Coldstart.Web.Tests/LeadsPageTests.cs`, `tests/Coldstart.Web.Tests/ColdstartWebFactory.cs`

**Interfaces:**
- Consumes: `AddApplication()` (Task 5), `AddInfrastructure(conn)` + `DbSeeder` (Tasks 6–7), `IListLeads`, `ICustomerRepository`.
- Produces: a running app serving `GET /Leads` that lists seeded leads; `ColdstartWebFactory` test harness pointing the app at a Testcontainers Postgres.

- [ ] **Step 1: Configure the composition root**

Replace `src/Coldstart.Web/Program.cs`:

```csharp
using Coldstart.Application;
using Coldstart.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("Coldstart")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:Coldstart"));

var app = builder.Build();

// Dev convenience: apply migrations + seed on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, CancellationToken.None);
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.Run();

public partial class Program; // for WebApplicationFactory
```

Add the connection string to `src/Coldstart.Web/appsettings.Development.json` (gitignored):

```json
{
  "ConnectionStrings": {
    "Coldstart": "Host=localhost;Port=5432;Database=coldstart;Username=coldstart;Password=localdev"
  }
}
```

- [ ] **Step 2: Add the Razor page**

`src/Coldstart.Web/Pages/Leads.cshtml.cs`:

```csharp
using Coldstart.Application.Leads;
using Coldstart.Application.Ports;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Pages;

public sealed class LeadsModel(ICustomerRepository customers, IListLeads listLeads) : PageModel
{
    public string CustomerName { get; private set; } = "—";
    public IReadOnlyList<LeadSummary> Leads { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var all = await customers.AllAsync(ct);
        var customer = all.FirstOrDefault();
        if (customer is null) return;
        CustomerName = customer.Name;
        Leads = await listLeads.ForCustomerAsync(customer.Id, ct);
    }
}
```

`src/Coldstart.Web/Pages/Leads.cshtml`:

```html
@page
@model Coldstart.Web.Pages.LeadsModel
@{ ViewData["Title"] = "Leads"; }

<h1>Leads — @Model.CustomerName</h1>

<table class="table">
    <thead>
        <tr><th>Company</th><th>Fit</th><th>Status</th><th>Recipient</th><th>Confidence</th></tr>
    </thead>
    <tbody>
    @foreach (var lead in Model.Leads)
    {
        <tr>
            <td>@lead.Company</td>
            <td>@lead.Fit</td>
            <td>@lead.Status</td>
            <td>@(lead.RecipientEmail ?? "—")</td>
            <td>@lead.EmailConfidence</td>
        </tr>
    }
    </tbody>
</table>
```

- [ ] **Step 3: Write the failing smoke test**

```bash
dotnet add tests/Coldstart.Web.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/Coldstart.Web.Tests package Testcontainers.PostgreSql
```

`tests/Coldstart.Web.Tests/ColdstartWebFactory.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace Coldstart.Web.Tests;

public sealed class ColdstartWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db =
        new PostgreSqlBuilder().WithImage("postgres:17").Build();

    public Task InitializeAsync() => _db.StartAsync();

    async Task IAsyncLifetime.DisposeAsync() => await _db.DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Coldstart", _db.GetConnectionString());
    }
}
```

`tests/Coldstart.Web.Tests/LeadsPageTests.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class LeadsPageTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task GetLeads_ReturnsOk_AndShowsSeededCompany()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/Leads");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        html.Should().Contain("Patronus AI");
        html.Should().Contain("anand@patronus.ai");
    }
}
```

- [ ] **Step 4: Run to verify it fails, then passes**

Run: `dotnet test tests/Coldstart.Web.Tests`
Expected first: FAIL (page/route or factory missing). After Steps 1–2 are in place it should PASS (migrations + seed run against the Testcontainers DB, `/Leads` renders the seeded rows).

- [ ] **Step 5: Manual run (optional sanity check)**

```bash
docker compose up -d db
dotnet run --project src/Coldstart.Web
# open the printed URL + /Leads — should list the 3 seeded leads
```

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Web tests/Coldstart.Web.Tests
git commit -m "feat(web): Razor Leads page + composition root + smoke test"
```

---

## Self-Review

**Spec coverage:**
- Hexagonal 4-project solution → Task 1. ✓
- Domain model (Customer/Lead, VOs, behaviour, DomainException) → Tasks 2–4. ✓
- EF Core + Postgres behind ports, VO/enum mapping, async+CancellationToken → Task 6. ✓
- Migrations + seed (demo customer incl. andrzej.lech.andrzej@gmail.com context) → Task 7. ✓ *(note: seeder uses the Coldstart demo customer; the customer's own recipient address is a later-SP concern and not required for the SP1 page)*
- Razor page listing seeded leads (Web→App→Infra→Postgres) → Task 8. ✓
- Tests: domain unit, application use case, repository integration (Testcontainers), web smoke → Tasks 2–8. ✓
- Standards (records for VOs, private setters/factories, ports in Application, DI only in Web, internal + InternalsVisibleTo, IReadOnlyList not IQueryable) → applied throughout. ✓

**Placeholder scan:** none — every step has concrete code/commands. (Draft bodies in the seeder use "…" intentionally as seed content, not a plan placeholder.)

**Type consistency:** `ILeadRepository.ForCustomerAsync`/`AddAsync`, `ICustomerRepository.AllAsync`/`AddAsync`, `IListLeads.ForCustomerAsync`, `LeadSummary(Guid,string,string,string,string?,string)`, `AddApplication()`, `AddInfrastructure(string)`, `DbSeeder.SeedAsync(ColdstartDbContext, CancellationToken)` — used consistently across Tasks 5–8. ✓

# Structured Targeting Profile Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the flat keyword bag that describes who a customer sells to with a structured, human-corrected targeting profile, editable by the customer in settings and by the operator on the admin customer page.

**Architecture:** A new `Targeting` value object owned by `Customer`, held separately from the already-overloaded `CompanyProfile`. Lists are stored newline-joined (the existing convention) and exposed as computed list accessors, so the EF owned-type mapping needs no value converters. `HarvestTargeting` gains a structured path that, in this sub-project, derives exactly the same harvest inputs as the legacy path — proven by test — so nothing about lead flow changes until sub-projects B and C consume the structure.

**Tech Stack:** .NET 10, Razor Pages, EF Core + Postgres (Npgsql), xUnit + NSubstitute + FluentAssertions, Groq LLM API.

**Spec:** `docs/superpowers/specs/2026-08-07-targeting-profile-design.md`

## Global Constraints

- CS-HEX-01: Domain has zero dependencies on other projects, EF Core, ASP.NET Core, or external SDKs.
- CS-HEX-02: ports live in Application, not Infrastructure. CS-HEX-03: DI wired in the composition root only.
- CS-DDD-01: value objects are `record`s. CS-DDD-02: entities protect state with `private set` and expose behaviour.
- CS-DATA-03: async all the way; `CancellationToken` threaded to IO. No `.Result` / `.Wait()`.
- CS-MOD-01: prefer `internal` over `public` for implementation classes.
- Tests: xUnit + NSubstitute + FluentAssertions, `MethodName_StateUnderTest_ExpectedBehavior`, strict `// Arrange` / `// Act` / `// Assert` comments, ≥1 happy + 2 sad paths per new class. Only mock outbound ports; never mock domain entities.
- Commit messages: conventional prefix; NEVER a `Co-Authored-By` trailer or any "Generated with Claude" line.
- Do NOT run `git push`. The operator does all pushes.
- UI uses the existing `.pz` design system in `src/Coldstart.Web/wwwroot/css/site.css` (`.pz-head`, `.pz-card`, `.pz-settings`, `.pz-field`, `.pz-actions`, `.pz-btn*`, `.pz-hint`, `.pz-empty`). Do not add ad-hoc CSS or inline `style=` for layout.
- Customer-facing copy: active voice, sentence case, plain verbs, no marketing tone.
- Run tests from the repo root: `dotnet test`, or `dotnet test tests/<Project> --filter <Name>`.

---

## Task 1: Domain — the `Targeting` value object

**Files:**
- Create: `src/Coldstart.Domain/Targeting.cs`
- Modify: `src/Coldstart.Domain/Customer.cs`
- Test: `tests/Coldstart.Domain.Tests/TargetingTests.cs` (new)

**Interfaces:**
- Consumes: nothing.
- Produces: `Targeting` record with `Industries`, `Geographies`, `BuyerRoles`, `TriggerEvents`, `Exclusions` (all `string?`, newline-joined), `SizeMin`/`SizeMax` (`int?`), computed `IndustryList` / `GeographyList` / `BuyerRoleList` / `TriggerEventList` / `ExclusionList` (`IReadOnlyList<string>`), `Targeting.Empty`, `Targeting.Create(...)`, and `HasAny`. Also `Customer.Targeting` (`Targeting`, private set) and `Customer.UpdateTargeting(Targeting)`. Every later task depends on these exact names.

- [ ] **Step 1: Write the failing tests**

`tests/Coldstart.Domain.Tests/TargetingTests.cs`:

```csharp
using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class TargetingTests
{
    [Fact]
    public void Create_ListWithBlanksAndDuplicates_NormalisesToTrimmedDistinctEntries()
    {
        // Arrange
        string[] industries = ["  logistics ", "logistics", "", "   ", "3PL"];

        // Act
        var targeting = Targeting.Create(industries, [], [], null, null, [], []);

        // Assert
        targeting.IndustryList.Should().Equal("logistics", "3PL");
    }

    [Fact]
    public void Create_NoValuesAtAll_IsEmptyAndHasNothing()
    {
        // Act
        var targeting = Targeting.Create([], [], [], null, null, [], []);

        // Assert
        targeting.HasAny.Should().BeFalse();
        targeting.IndustryList.Should().BeEmpty();
    }

    [Fact]
    public void Create_AnySingleDimensionPopulated_HasAnyIsTrue()
    {
        // Act
        var targeting = Targeting.Create([], [], [], null, null, ["raised funding"], []);

        // Assert
        targeting.HasAny.Should().BeTrue();
        targeting.TriggerEventList.Should().Equal("raised funding");
    }

    [Fact]
    public void Create_SizeMinGreaterThanSizeMax_Throws()
    {
        // Act
        Action act = () => Targeting.Create([], [], [], sizeMin: 200, sizeMax: 10, [], []);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("*smallest company size*");
    }

    [Fact]
    public void Create_NegativeSize_Throws()
    {
        // Act
        Action act = () => Targeting.Create([], [], [], sizeMin: -1, sizeMax: null, [], []);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("*cannot be negative*");
    }

    [Fact]
    public void Create_OnlyOneEndOfTheSizeBand_IsAllowed()
    {
        // Act
        var targeting = Targeting.Create([], [], [], sizeMin: 10, sizeMax: null, [], []);

        // Assert
        targeting.SizeMin.Should().Be(10);
        targeting.SizeMax.Should().BeNull();
        targeting.HasAny.Should().BeTrue();
    }

    [Fact]
    public void UpdateTargeting_OnCustomer_ReplacesTheStoredTargeting()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        var targeting = Targeting.Create(["logistics"], ["NL"], ["Fleet Manager"], 10, 200, ["raised funding"], ["competitors"]);

        // Act
        customer.UpdateTargeting(targeting);

        // Assert
        customer.Targeting.Should().Be(targeting);
        customer.Targeting.GeographyList.Should().Equal("NL");
    }

    [Fact]
    public void Customer_Created_StartsWithEmptyTargeting()
    {
        // Act
        var customer = Customer.Create("Acme");

        // Assert
        customer.Targeting.Should().Be(Targeting.Empty);
        customer.Targeting.HasAny.Should().BeFalse();
    }

    [Fact]
    public void UpdateTargeting_WithNull_FallsBackToEmptyRatherThanNulling()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        customer.UpdateTargeting(Targeting.Create(["logistics"], [], [], null, null, [], []));

        // Act
        customer.UpdateTargeting(null!);

        // Assert
        customer.Targeting.Should().Be(Targeting.Empty);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Coldstart.Domain.Tests --filter "TargetingTests"`
Expected: FAIL to compile — `Targeting` does not exist.

- [ ] **Step 3: Implement `Targeting`**

`src/Coldstart.Domain/Targeting.cs`:

```csharp
namespace Coldstart.Domain;

// Who a customer sells to, structured so each pipeline stage can read the part it
// needs: query generation wants industries/geographies/triggers, the cheap gate wants
// geography/size/exclusions, the draft prompt wants the trigger for an honest "why now".
// Lists are stored newline-joined to match CompanyProfile's existing convention, which
// keeps the EF owned-type mapping free of value converters.
public sealed record Targeting(
    string? Industries,
    string? Geographies,
    string? BuyerRoles,
    int? SizeMin,
    int? SizeMax,
    string? TriggerEvents,
    string? Exclusions)
{
    public static Targeting Empty { get; } = new(null, null, null, null, null, null, null);

    public IReadOnlyList<string> IndustryList => Split(Industries);
    public IReadOnlyList<string> GeographyList => Split(Geographies);
    public IReadOnlyList<string> BuyerRoleList => Split(BuyerRoles);
    public IReadOnlyList<string> TriggerEventList => Split(TriggerEvents);
    public IReadOnlyList<string> ExclusionList => Split(Exclusions);

    // Empty targeting is a valid state: the harvest falls back to the legacy flat fields,
    // so a customer who never fills the form keeps working exactly as before.
    public bool HasAny =>
        IndustryList.Count > 0 || GeographyList.Count > 0 || BuyerRoleList.Count > 0
        || TriggerEventList.Count > 0 || ExclusionList.Count > 0
        || SizeMin is not null || SizeMax is not null;

    public static Targeting Create(
        IEnumerable<string> industries,
        IEnumerable<string> geographies,
        IEnumerable<string> buyerRoles,
        int? sizeMin,
        int? sizeMax,
        IEnumerable<string> triggerEvents,
        IEnumerable<string> exclusions)
    {
        if (sizeMin is < 0 || sizeMax is < 0)
            throw new DomainException("A company size cannot be negative.");
        if (sizeMin is { } min && sizeMax is { } max && min > max)
            throw new DomainException("The smallest company size must not be larger than the largest.");

        return new Targeting(Join(industries), Join(geographies), Join(buyerRoles),
            sizeMin, sizeMax, Join(triggerEvents), Join(exclusions));
    }

    private static string? Join(IEnumerable<string> values)
    {
        var cleaned = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return cleaned.Count == 0 ? null : string.Join('\n', cleaned);
    }

    private static IReadOnlyList<string> Split(string? joined) =>
        string.IsNullOrWhiteSpace(joined)
            ? []
            : joined.Split(['\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
```

- [ ] **Step 4: Add the property and behaviour to `Customer`**

In `src/Coldstart.Domain/Customer.cs`, add the property next to `Profile` (around line 9):

```csharp
    public Targeting Targeting { get; private set; } = Targeting.Empty;
```

and the behaviour next to `UpdateProfile` (around line 40):

```csharp
    public void UpdateTargeting(Targeting targeting) => Targeting = targeting ?? Targeting.Empty;
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Coldstart.Domain.Tests`
Expected: ALL PASS, including every pre-existing domain test.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Domain/Targeting.cs src/Coldstart.Domain/Customer.cs tests/Coldstart.Domain.Tests/TargetingTests.cs
git commit -m "feat(domain): structured targeting profile value object"
```

---

## Task 2: EF mapping + migration

**Files:**
- Modify: `src/Coldstart.Infrastructure/Configurations/CustomerConfiguration.cs`
- Create: `src/Coldstart.Infrastructure/Migrations/<timestamp>_AddCustomerTargeting.cs` (generated)

**Interfaces:**
- Consumes: `Customer.Targeting` and the `Targeting` record from Task 1.
- Produces: seven nullable columns on `customers` (`TargetingIndustries`, `TargetingGeographies`, `TargetingBuyerRoles`, `TargetingSizeMin`, `TargetingSizeMax`, `TargetingTriggerEvents`, `TargetingExclusions`), persisted via `ICustomerRepository.UpdateAsync`.

- [ ] **Step 1: Map the owned type**

In `CustomerConfiguration.Configure`, after the existing `builder.OwnsOne(c => c.Profile, …)` block closes, add — mirroring that block's column-prefix style:

```csharp
        builder.OwnsOne(c => c.Targeting, t =>
        {
            t.Property(x => x.Industries).HasColumnName("TargetingIndustries");
            t.Property(x => x.Geographies).HasColumnName("TargetingGeographies");
            t.Property(x => x.BuyerRoles).HasColumnName("TargetingBuyerRoles");
            t.Property(x => x.SizeMin).HasColumnName("TargetingSizeMin");
            t.Property(x => x.SizeMax).HasColumnName("TargetingSizeMax");
            t.Property(x => x.TriggerEvents).HasColumnName("TargetingTriggerEvents");
            t.Property(x => x.Exclusions).HasColumnName("TargetingExclusions");
        });
```

and next to the existing `builder.Navigation(c => c.Profile).IsRequired();` line add:

```csharp
        builder.Navigation(c => c.Targeting).IsRequired();   // never null; defaults to Targeting.Empty
```

- [ ] **Step 2: Generate the migration**

Inspect the most recent migration commit for the exact invocation this repo uses (`git log --oneline --diff-filter=A -1 -- 'src/Coldstart.Infrastructure/Migrations/*AddCustomerComplimentaryUntil*'`, then `git show <sha> --stat`), then run the equivalent of:

```bash
dotnet ef migrations add AddCustomerTargeting \
  -p src/Coldstart.Infrastructure -s src/Coldstart.Infrastructure -o Migrations
```

- [ ] **Step 3: Verify the generated migration**

`Up()` must contain exactly seven `AddColumn` calls on the `customers` table — five `string` (nullable) and two `int` (nullable) — with the matching `DropColumn` calls in `Down()`, and nothing else. No defaults, no indexes, no data movement. If it contains anything more, stop and report rather than editing the generated file by hand.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: ALL PASS. The Web tests run against a real Postgres container, so this proves the migration applies cleanly.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Infrastructure/Configurations/CustomerConfiguration.cs src/Coldstart.Infrastructure/Migrations/
git commit -m "feat(db): targeting columns on customers"
```

---

## Task 3: Application — read/write use cases

**Files:**
- Create: `src/Coldstart.Application/Customers/TargetingInput.cs`
- Create: `src/Coldstart.Application/Customers/TargetingView.cs`
- Create: `src/Coldstart.Application/Customers/IUpdateTargeting.cs`
- Create: `src/Coldstart.Application/Customers/UpdateTargeting.cs`
- Create: `src/Coldstart.Application/Customers/IGetTargeting.cs`
- Create: `src/Coldstart.Application/Customers/GetTargeting.cs`
- Modify: `src/Coldstart.Application/DependencyInjection.cs`
- Test: `tests/Coldstart.Application.Tests/TargetingUseCasesTests.cs` (new)

**Interfaces:**
- Consumes: `Targeting.Create(...)`, `Customer.UpdateTargeting(...)`, `Customer.Targeting` (Task 1); `ICustomerRepository.GetAsync/UpdateAsync`.
- Produces:
  - `TargetingInput(string? Industries, string? Geographies, string? BuyerRoles, int? SizeMin, int? SizeMax, string? TriggerEvents, string? Exclusions)` — raw textarea text, one entry per line, exactly as typed.
  - `TargetingView(string Industries, string Geographies, string BuyerRoles, int? SizeMin, int? SizeMax, string TriggerEvents, string Exclusions)` — newline-joined text for rendering into textareas; empty string rather than null.
  - `IUpdateTargeting.UpdateAsync(Guid customerId, TargetingInput input, CancellationToken ct)` returning `TargetingResult(bool Succeeded, string? Message)`.
  - `IGetTargeting.ForCustomerAsync(Guid customerId, CancellationToken ct)` returning `TargetingView?`.
  Both the customer settings page and the admin editor call these — this is the single shared path the spec requires.

- [ ] **Step 1: Write the failing tests**

`tests/Coldstart.Application.Tests/TargetingUseCasesTests.cs`:

```csharp
using Coldstart.Application.Customers;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class TargetingUseCasesTests
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();

    [Fact]
    public async Task UpdateAsync_MultilineInput_StoresNormalisedTargeting()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        _customers.GetAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new UpdateTargeting(_customers);
        var input = new TargetingInput("logistics\n 3PL \nlogistics", "NL\nBE", "Fleet Manager", 10, 200,
            "raised funding", "competitors");

        // Act
        var result = await sut.UpdateAsync(customer.Id.Value, input, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        customer.Targeting.IndustryList.Should().Equal("logistics", "3PL");
        customer.Targeting.GeographyList.Should().Equal("NL", "BE");
        await _customers.Received(1).UpdateAsync(customer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_InvertedSizeBand_ReturnsFieldErrorAndPersistsNothing()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        _customers.GetAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new UpdateTargeting(_customers);
        var input = new TargetingInput(null, null, null, 200, 10, null, null);

        // Act
        var result = await sut.UpdateAsync(customer.Id.Value, input, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("smallest company size");
        await _customers.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_UnknownCustomer_ReturnsNotFoundAndPersistsNothing()
    {
        // Arrange
        _customers.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        var sut = new UpdateTargeting(_customers);

        // Act
        var result = await sut.UpdateAsync(Guid.NewGuid(), new TargetingInput(null, null, null, null, null, null, null),
            CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        await _customers.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_AllFieldsCleared_IsAllowedAndLeavesEmptyTargeting()
    {
        // Arrange — clearing the form must be legal; the harvest falls back to legacy fields
        var customer = Customer.Create("Acme");
        customer.UpdateTargeting(Targeting.Create(["logistics"], [], [], null, null, [], []));
        _customers.GetAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new UpdateTargeting(_customers);

        // Act
        var result = await sut.UpdateAsync(customer.Id.Value,
            new TargetingInput("", "", "", null, null, "", ""), CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        customer.Targeting.HasAny.Should().BeFalse();
    }

    [Fact]
    public async Task ForCustomerAsync_StoredTargeting_ReturnsTextForTextareas()
    {
        // Arrange
        var customer = Customer.Create("Acme");
        customer.UpdateTargeting(Targeting.Create(["logistics", "3PL"], ["NL"], [], 10, null, [], []));
        _customers.GetAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var sut = new GetTargeting(_customers);

        // Act
        var view = await sut.ForCustomerAsync(customer.Id.Value, CancellationToken.None);

        // Assert
        view.Should().NotBeNull();
        view!.Industries.Should().Be("logistics\n3PL");
        view.BuyerRoles.Should().BeEmpty();
        view.SizeMin.Should().Be(10);
    }

    [Fact]
    public async Task ForCustomerAsync_UnknownCustomer_ReturnsNull()
    {
        // Arrange
        _customers.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);
        var sut = new GetTargeting(_customers);

        // Act
        var view = await sut.ForCustomerAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        view.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Coldstart.Application.Tests --filter "TargetingUseCasesTests"`
Expected: FAIL to compile — the types do not exist.

- [ ] **Step 3: Implement the records and use cases**

`src/Coldstart.Application/Customers/TargetingInput.cs`:

```csharp
namespace Coldstart.Application.Customers;

// Raw form text: one entry per line, exactly as typed. Normalising is the domain's job.
public sealed record TargetingInput(
    string? Industries,
    string? Geographies,
    string? BuyerRoles,
    int? SizeMin,
    int? SizeMax,
    string? TriggerEvents,
    string? Exclusions);

public sealed record TargetingResult(bool Succeeded, string? Message = null);
```

`src/Coldstart.Application/Customers/TargetingView.cs`:

```csharp
namespace Coldstart.Application.Customers;

// Newline-joined text ready to drop into a textarea. Empty string, never null, so the
// view never has to null-check.
public sealed record TargetingView(
    string Industries,
    string Geographies,
    string BuyerRoles,
    int? SizeMin,
    int? SizeMax,
    string TriggerEvents,
    string Exclusions);
```

`src/Coldstart.Application/Customers/IUpdateTargeting.cs`:

```csharp
namespace Coldstart.Application.Customers;

public interface IUpdateTargeting
{
    Task<TargetingResult> UpdateAsync(Guid customerId, TargetingInput input, CancellationToken ct);
}
```

`src/Coldstart.Application/Customers/UpdateTargeting.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

// The one write path shared by the customer settings page and the admin editor, so the
// two surfaces cannot drift apart in validation or behaviour.
internal sealed class UpdateTargeting(ICustomerRepository customers) : IUpdateTargeting
{
    public async Task<TargetingResult> UpdateAsync(Guid customerId, TargetingInput input, CancellationToken ct)
    {
        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is null) return new(false, "No such customer.");

        Targeting targeting;
        try
        {
            targeting = Targeting.Create(
                Lines(input.Industries), Lines(input.Geographies), Lines(input.BuyerRoles),
                input.SizeMin, input.SizeMax, Lines(input.TriggerEvents), Lines(input.Exclusions));
        }
        catch (DomainException ex)
        {
            return new(false, ex.Message);
        }

        customer.UpdateTargeting(targeting);
        await customers.UpdateAsync(customer, ct);
        return new(true);
    }

    private static IEnumerable<string> Lines(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
```

`src/Coldstart.Application/Customers/IGetTargeting.cs`:

```csharp
namespace Coldstart.Application.Customers;

public interface IGetTargeting
{
    Task<TargetingView?> ForCustomerAsync(Guid customerId, CancellationToken ct);
}
```

`src/Coldstart.Application/Customers/GetTargeting.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

internal sealed class GetTargeting(ICustomerRepository customers) : IGetTargeting
{
    public async Task<TargetingView?> ForCustomerAsync(Guid customerId, CancellationToken ct)
    {
        var customer = await customers.GetAsync(new CustomerId(customerId), ct);
        if (customer is null) return null;
        var t = customer.Targeting;
        return new TargetingView(t.Industries ?? "", t.Geographies ?? "", t.BuyerRoles ?? "",
            t.SizeMin, t.SizeMax, t.TriggerEvents ?? "", t.Exclusions ?? "");
    }
}
```

Register both in `src/Coldstart.Application/DependencyInjection.cs`, next to the existing `IUpdateCompanyProfile` / `IGetCompanyProfile` registrations and in the same style:

```csharp
        services.AddScoped<Customers.IUpdateTargeting, Customers.UpdateTargeting>();
        services.AddScoped<Customers.IGetTargeting, Customers.GetTargeting>();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Coldstart.Application.Tests`
Expected: ALL PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Application/Customers/ src/Coldstart.Application/DependencyInjection.cs tests/Coldstart.Application.Tests/TargetingUseCasesTests.cs
git commit -m "feat(targeting): read and write use cases shared by both editors"
```

---

## Task 4: Harvest compatibility — structured path that changes nothing

This is the load-bearing task of the sub-project. It proves that adding structured targeting does not alter what gets harvested for anybody.

**Files:**
- Modify: `src/Coldstart.Application/Harvest/HarvestTargeting.cs`
- Modify: `src/Coldstart.Application/Harvest/HarvestLeads.cs` (the call site that builds targeting from the customer)
- Test: `tests/Coldstart.Application.Tests/HarvestTargetingCompatibilityTests.cs` (new)

**Interfaces:**
- Consumes: `Customer.Targeting`, `Targeting.HasAny`, the list accessors (Task 1); the existing `HarvestTargeting.FromProfile(CompanyProfile, string customerName)`.
- Produces: `HarvestTargeting.FromCustomer(CompanyProfile profile, Targeting targeting, string customerName)` — picks the structured path when `targeting.HasAny`, otherwise delegates to `FromProfile`. Sub-projects B and C extend the structured branch; in this task it must produce equivalent output.

- [ ] **Step 1: Write the failing tests**

`tests/Coldstart.Application.Tests/HarvestTargetingCompatibilityTests.cs`:

```csharp
using Coldstart.Application.Harvest;
using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class HarvestTargetingCompatibilityTests
{
    private static CompanyProfile ProfileWith(string keywords, string signals, string disqualifiers) =>
        CompanyProfile.Empty with
        {
            CompanyName = "Acme",
            Offer = "route planning software",
            TargetKeywords = keywords,
            BuyingSignals = signals,
            Disqualifiers = disqualifiers,
        };

    [Fact]
    public void FromCustomer_NoStructuredTargeting_MatchesTheLegacyPathExactly()
    {
        // Arrange
        var profile = ProfileWith("logistics\n3PL", "raised funding", "competitors");

        // Act
        var legacy = HarvestTargeting.FromProfile(profile, "Acme");
        var actual = HarvestTargeting.FromCustomer(profile, Targeting.Empty, "Acme");

        // Assert — byte-for-byte the same behaviour for every existing customer
        actual.Should().BeEquivalentTo(legacy);
    }

    [Fact]
    public void FromCustomer_StructuredTargetingWithSameData_AgreesWithTheLegacyPath()
    {
        // Arrange — the same intent expressed both ways
        var profile = ProfileWith("logistics\n3PL", "raised funding", "competitors");
        var targeting = Targeting.Create(["logistics", "3PL"], [], [], null, null, ["raised funding"], ["competitors"]);

        // Act
        var legacy = HarvestTargeting.FromProfile(profile, "Acme");
        var structured = HarvestTargeting.FromCustomer(profile, targeting, "Acme");

        // Assert — sub-project A must not move lead flow in either direction
        structured.TargetKeywords.Should().BeEquivalentTo(legacy.TargetKeywords);
        structured.BuyingSignals.Should().BeEquivalentTo(legacy.BuyingSignals);
        structured.Disqualifiers.Should().BeEquivalentTo(legacy.Disqualifiers);
        structured.Offer.Should().Be(legacy.Offer);
    }

    [Fact]
    public void FromCustomer_StructuredTargeting_DrawsKeywordsFromIndustriesAndGeographies()
    {
        // Arrange
        var profile = ProfileWith("", "", "");
        var targeting = Targeting.Create(["logistics"], ["NL"], ["Fleet Manager"], 10, 200,
            ["raised funding"], ["competitors"]);

        // Act
        var actual = HarvestTargeting.FromCustomer(profile, targeting, "Acme");

        // Assert
        actual.TargetKeywords.Should().Contain("logistics");
        actual.BuyingSignals.Should().Contain("raised funding");
        actual.Disqualifiers.Should().Contain("competitors");
        actual.HasKeywords.Should().BeTrue();
    }

    [Fact]
    public void FromCustomer_EmptyProfileAndEmptyTargeting_ProducesNoKeywords()
    {
        // Arrange
        var profile = ProfileWith("", "", "");

        // Act
        var actual = HarvestTargeting.FromCustomer(profile, Targeting.Empty, "Acme");

        // Assert — the harvest's own "no keywords" guard still applies downstream
        actual.HasKeywords.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Coldstart.Application.Tests --filter "HarvestTargetingCompatibilityTests"`
Expected: FAIL to compile — `FromCustomer` does not exist.

- [ ] **Step 3: Implement `FromCustomer`**

Add to `src/Coldstart.Application/Harvest/HarvestTargeting.cs`, beside the existing factories:

```csharp
    // Structured targeting when the customer has filled it in, otherwise the legacy flat
    // fields, unchanged. In sub-project A the structured branch deliberately derives the
    // same inputs as the legacy one — sub-projects B (query generation) and C
    // (qualification) are what actually exploit the extra dimensions.
    public static HarvestTargeting FromCustomer(CompanyProfile profile, Targeting targeting, string customerName)
    {
        if (targeting is null || !targeting.HasAny) return FromProfile(profile, customerName);

        var keywords = targeting.IndustryList.Concat(targeting.GeographyList).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (keywords.Count == 0) keywords = [.. Split(profile.TargetKeywords)];

        var signals = targeting.TriggerEventList.Count > 0
            ? targeting.TriggerEventList
            : Split(profile.BuyingSignals);

        var exclusions = targeting.ExclusionList.Count > 0
            ? targeting.ExclusionList
            : Split(profile.Disqualifiers);

        return new(
            profile.Offer ?? "",
            keywords,
            signals,
            exclusions,
            SenderName: string.IsNullOrWhiteSpace(profile.CompanyName) ? customerName : profile.CompanyName!,
            SenderTitle: "",
            MinScore: 0.4,
            MaxEnrichPerRun: 30);
    }
```

- [ ] **Step 4: Switch the harvest call site**

In `src/Coldstart.Application/Harvest/HarvestLeads.cs`, find where per-customer targeting is built for a harvest run and route it through `FromCustomer(customer.Profile, customer.Targeting, customer.Name)` instead of `FromProfile(customer.Profile, customer.Name)`. Read the surrounding method first: the console path (`RunAsync`) and the per-customer path (`RunForCustomerAsync`) resolve their customer differently, so change only the per-customer path and leave the console path on `FromNiche` as it is.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: ALL PASS. Every pre-existing harvest test must still pass untouched — if one needs changing, stop: that means behaviour moved, which this task exists to prevent.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Application/Harvest/ tests/Coldstart.Application.Tests/HarvestTargetingCompatibilityTests.cs
git commit -m "feat(targeting): harvest reads structured targeting with legacy fallback"
```

---

## Task 5: Generation — propose a profile from prose

**Files:**
- Create: `src/Coldstart.Application/Customers/ITargetingGenerator.cs`
- Create: `src/Coldstart.Application/Customers/TargetingProposalParser.cs`
- Create: `src/Coldstart.Infrastructure/Harvest/GroqTargetingGenerator.cs`
- Modify: `src/Coldstart.Infrastructure/DependencyInjection.cs`
- Test: `tests/Coldstart.Application.Tests/TargetingProposalParserTests.cs` (new), `tests/Coldstart.Infrastructure.Tests/GroqTargetingGeneratorTests.cs` (new)

**Interfaces:**
- Consumes: nothing from earlier tasks except `TargetingInput` (Task 3), which is what a proposal fills.
- Produces:
  - `TargetingProposalRequest(string CompanyName, string Niche, string Brief, string Offer)`.
  - `ITargetingGenerator.ProposeAsync(TargetingProposalRequest request, CancellationToken ct)` returning `TargetingInput?` — null on any failure.
  - `TargetingProposalParser.Parse(string content)` returning `TargetingInput?`.

- [ ] **Step 1: Write the failing parser tests**

`tests/Coldstart.Application.Tests/TargetingProposalParserTests.cs`:

```csharp
using Coldstart.Application.Customers;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class TargetingProposalParserTests
{
    [Fact]
    public void Parse_LabelledOutput_FillsEveryField()
    {
        // Arrange
        var content = """
            INDUSTRIES: logistics, 3PL
            GEOGRAPHIES: Netherlands, Belgium
            ROLES: Fleet Manager, Head of Operations
            SIZE_MIN: 10
            SIZE_MAX: 200
            TRIGGERS: raised funding, hiring fleet staff
            EXCLUSIONS: competitors, consultancies
            """;

        // Act
        var input = TargetingProposalParser.Parse(content);

        // Assert
        input.Should().NotBeNull();
        input!.Industries.Should().Contain("logistics").And.Contain("3PL");
        input.SizeMin.Should().Be(10);
        input.SizeMax.Should().Be(200);
        input.TriggerEvents.Should().Contain("raised funding");
        input.Exclusions.Should().Contain("competitors");
    }

    [Fact]
    public void Parse_MissingOptionalSections_StillReturnsWhatWasGiven()
    {
        // Arrange
        var content = "INDUSTRIES: logistics\nTRIGGERS: raised funding";

        // Act
        var input = TargetingProposalParser.Parse(content);

        // Assert
        input.Should().NotBeNull();
        input!.Industries.Should().Contain("logistics");
        input.Geographies.Should().BeNullOrEmpty();
        input.SizeMin.Should().BeNull();
    }

    [Fact]
    public void Parse_NoRecognisableLabels_ReturnsNullRatherThanAnEmptyProfile()
    {
        // Act
        var input = TargetingProposalParser.Parse("I'm sorry, I can't help with that.");

        // Assert — an unparseable response is a failure, never a silently empty profile
        input.Should().BeNull();
    }

    [Fact]
    public void Parse_NonNumericSize_IgnoresTheSizeRatherThanFailingTheWholeProposal()
    {
        // Arrange
        var content = "INDUSTRIES: logistics\nSIZE_MIN: lots";

        // Act
        var input = TargetingProposalParser.Parse(content);

        // Assert
        input.Should().NotBeNull();
        input!.SizeMin.Should().BeNull();
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsNull()
    {
        // Act / Assert
        TargetingProposalParser.Parse("").Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Coldstart.Application.Tests --filter "TargetingProposalParserTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Implement the port and parser**

`src/Coldstart.Application/Customers/ITargetingGenerator.cs`:

```csharp
namespace Coldstart.Application.Customers;

public sealed record TargetingProposalRequest(string CompanyName, string Niche, string Brief, string Offer);

// Proposes a structured targeting profile from the customer's own prose. Best-effort:
// returns null rather than throwing, and the caller never persists a proposal without a
// human approving it.
public interface ITargetingGenerator
{
    Task<TargetingInput?> ProposeAsync(TargetingProposalRequest request, CancellationToken ct);
}
```

`src/Coldstart.Application/Customers/TargetingProposalParser.cs` — model it closely on the existing `SearchProfileParser` in the same folder (read that file first and mirror its label-slicing approach):

```csharp
using System.Text.RegularExpressions;

namespace Coldstart.Application.Customers;

// Parses the LLM's labelled output into a TargetingInput. At least one recognisable
// label is required; anything else returns null so an unparseable answer is treated as a
// failure rather than as an empty profile.
public static class TargetingProposalParser
{
    private static readonly string[] Labels =
        ["INDUSTRIES", "GEOGRAPHIES", "ROLES", "SIZE_MIN", "SIZE_MAX", "TRIGGERS", "EXCLUSIONS"];

    public static TargetingInput? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var positions = new List<(int LabelStart, int ContentStart, string Label)>();
        foreach (var label in Labels)
        {
            var m = new Regex($@"^\s*{label}\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Multiline).Match(content);
            if (m.Success) positions.Add((m.Index, m.Index + m.Length, label));
        }
        if (positions.Count == 0) return null;
        positions.Sort((a, b) => a.LabelStart.CompareTo(b.LabelStart));

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < positions.Count; i++)
        {
            var (_, contentStart, label) = positions[i];
            var end = i + 1 < positions.Count ? positions[i + 1].LabelStart : content.Length;
            fields[label] = content[contentStart..end].Trim();
        }

        string? List(string label) =>
            fields.TryGetValue(label, out var v) && !string.IsNullOrWhiteSpace(v)
                ? string.Join('\n', v.Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                : null;

        int? Number(string label) =>
            fields.TryGetValue(label, out var v) && int.TryParse(v.Trim(), out var n) ? n : null;

        return new TargetingInput(List("INDUSTRIES"), List("GEOGRAPHIES"), List("ROLES"),
            Number("SIZE_MIN"), Number("SIZE_MAX"), List("TRIGGERS"), List("EXCLUSIONS"));
    }
}
```

- [ ] **Step 4: Implement the Groq generator**

`src/Coldstart.Infrastructure/Harvest/GroqTargetingGenerator.cs` — read `src/Coldstart.Infrastructure/Harvest/GroqSearchProfileGenerator.cs` first and mirror its HTTP call, options usage, retry/soft-fail behaviour and null-on-failure contract exactly. The only differences are the prompt and the parser it calls (`TargetingProposalParser.Parse`).

The prompt must ask for exactly the seven labels the parser knows (`INDUSTRIES`, `GEOGRAPHIES`, `ROLES`, `SIZE_MIN`, `SIZE_MAX`, `TRIGGERS`, `EXCLUSIONS`), one per line, comma-separated values, and must instruct the model to leave a label out entirely when it cannot infer it rather than guessing — a wrong industry is worse than a missing one, because it silently sends the engine hunting in the wrong market.

Register it in `src/Coldstart.Infrastructure/DependencyInjection.cs` beside the existing `ISearchProfileGenerator` registration:

```csharp
        services.AddScoped<Application.Customers.ITargetingGenerator, Harvest.GroqTargetingGenerator>();
```

- [ ] **Step 5: Write the generator tests**

`tests/Coldstart.Infrastructure.Tests/GroqTargetingGeneratorTests.cs` — reuse the fake `HttpMessageHandler` pattern already used by `GroqSearchProfileGeneratorTests` in the same folder. Cover: a well-formed labelled response yields a populated `TargetingInput`; a non-success HTTP status yields null; a success response whose body has no recognisable labels yields null; and a missing API key yields null without any HTTP call.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Coldstart.Application.Tests tests/Coldstart.Infrastructure.Tests`
Expected: ALL PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Coldstart.Application/Customers/ src/Coldstart.Infrastructure/Harvest/GroqTargetingGenerator.cs src/Coldstart.Infrastructure/DependencyInjection.cs tests/
git commit -m "feat(targeting): LLM proposes a structured profile from the customer's prose"
```

---

## Task 6: Customer settings — the Targeting page

**Files:**
- Create: `src/Coldstart.Web/Areas/App/Pages/Settings/Targeting.cshtml` + `.cshtml.cs`
- Modify: `src/Coldstart.Web/Areas/App/Pages/Shared/_AppSidebar.cshtml`
- Test: `tests/Coldstart.Web.Tests/TargetingSettingsTests.cs` (new)

**Interfaces:**
- Consumes: `IGetTargeting.ForCustomerAsync`, `IUpdateTargeting.UpdateAsync`, `TargetingInput`, `TargetingView` (Task 3); `ITargetingGenerator.ProposeAsync` (Task 5).
- Produces: route `/app/settings/targeting`, rail nav key `"targeting"`.

- [ ] **Step 1: Write the failing tests**

`tests/Coldstart.Web.Tests/TargetingSettingsTests.cs` — follow the sign-in and antiforgery-form helpers already used by `CustomerZoneTests` in the same project. Cover:

```csharp
[Fact]
public async Task Targeting_Get_RendersTheFormWithTheRailItemActive()
{
    // Arrange
    var client = factory.CreateClient();
    var email = $"targeting-{Guid.NewGuid():N}@test.com";
    await TestAuth.SignInAsync(factory, client, email, "Passw0rd!long");

    // Act
    var html = await client.GetStringAsync("/app/settings/targeting");

    // Assert
    html.Should().Contain("class=\"rail-item active\" href=\"/app/settings/targeting\"");
    html.Should().Contain("Industries");
    html.Should().Contain("Trigger events");
}

[Fact]
public async Task Targeting_Post_SavesAndShowsTheStoredValuesOnReload()
{
    // Arrange — sign in, then post industries/geographies/triggers
    // Act — POST the form, then GET the page again
    // Assert — the saved values come back in the textareas
}

[Fact]
public async Task Targeting_PostInvertedSizeBand_ShowsTheFieldErrorAndSavesNothing()
{
    // Assert — the response contains the "smallest company size" message and a reload
    // still shows the previous values
}
```

Fill in the two sketched bodies using the same helpers as the first test; do not leave them as comments.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "TargetingSettingsTests"`
Expected: FAIL — the route does not exist (404).

- [ ] **Step 3: Build the page**

Model the page on an existing settings page — read `src/Coldstart.Web/Areas/App/Pages/Settings/Company.cshtml` and its `.cshtml.cs` and follow their structure exactly: same rail shell wrapper (`.rail-shell` > `_AppSidebar` partial > `.rail-main` > `main.wrap.app-zone`), `ViewData["AppNav"] = "targeting";`, `.pz-head` with an h1 and a `.pz-sub`, `.pz-card` sections, `.pz-settings` grid, one `.pz-btn-primary` save in a `.pz-actions` footer, and the validation-summary and status-message rendering those pages already use.

Content:
- One card, "Who you sell to": Industries, Geographies, Buyer roles, Trigger events, Exclusions as textareas (one entry per line — say so in a `.pz-hint`), plus the two size fields as numbers.
- A secondary `.pz-btn-quiet` action, "Suggest from my description", posting to a handler that calls `ITargetingGenerator.ProposeAsync` with the customer's existing company name, niche, brief and offer, and fills the form fields **without saving**. Per the spec a proposal is never persisted silently — the human presses Save.
- When generation fails, keep the stored values on screen and show a plain message that the suggestion could not be produced.

Write the `.pz-sub` yourself: one honest line explaining that these fields tell the engine who to look for, and that leaving them empty keeps today's behaviour.

Add the rail entry in `_AppSidebar.cshtml` inside the Settings group, following the existing item pattern exactly:

```html
    <a class="rail-item @(nav == "targeting" ? "active" : null)" href="/app/settings/targeting">Targeting</a>
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Coldstart.Web.Tests`
Expected: ALL PASS. Note that adding a rail item may break a test that counts rail items — if so, update it to assert the new set rather than weakening it.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Web/Areas/App/Pages/Settings/Targeting.cshtml src/Coldstart.Web/Areas/App/Pages/Settings/Targeting.cshtml.cs src/Coldstart.Web/Areas/App/Pages/Shared/_AppSidebar.cshtml tests/Coldstart.Web.Tests/TargetingSettingsTests.cs
git commit -m "feat(app): customers edit their targeting profile in settings"
```

---

## Task 7: Admin — edit any customer's targeting

**Files:**
- Modify: `src/Coldstart.Web/Areas/Admin/Pages/Customers/Detail.cshtml` + `.cshtml.cs`
- Test: `tests/Coldstart.Web.Tests/CustomerDetailPageTests.cs` (extend)

**Interfaces:**
- Consumes: `IGetTargeting`, `IUpdateTargeting`, `TargetingInput` (Task 3) — the same use cases the customer page uses, which is what keeps the two surfaces consistent.
- Produces: a Targeting card on `/admin/customers/{id}` with its own save handler.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Coldstart.Web.Tests/CustomerDetailPageTests.cs`, reusing that file's existing SuperAdmin sign-in and customer-creation helpers:

```csharp
[Fact]
public async Task Detail_ShowsTheCustomersTargetingFields()
{
    // Arrange — SuperAdmin session and a customer
    // Act — GET /admin/customers/{id}
    // Assert
    html.Should().Contain("Targeting");
    html.Should().Contain("Industries");
}

[Fact]
public async Task Detail_SaveTargeting_PersistsAndIsVisibleToTheCustomersOwnPage()
{
    // Arrange — SuperAdmin saves targeting for a customer
    // Act — POST the admin targeting form, then read it back via the same use case
    // Assert — the stored values match what the admin entered, proving both surfaces
    // share one write path
}

[Fact]
public async Task Detail_SaveTargetingWithInvertedSizeBand_ShowsTheErrorAndSavesNothing()
{
    // Assert — the "smallest company size" message appears and nothing was stored
}
```

Fill in the sketched bodies with the real helpers; do not leave them as comments.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "CustomerDetailPageTests"`
Expected: the new tests FAIL; the pre-existing ones in that file still pass.

- [ ] **Step 3: Add the card**

Add a "Targeting" `.pz-card` to `Detail.cshtml`, matching the cards already on that page (Identity / People / Plan / Harvest). Same fields as the customer page, same one-primary-action rule, its own `asp-page-handler` posting to a handler on `Detail.cshtml.cs` that calls `IUpdateTargeting.UpdateAsync` and re-renders with the result message. Load the current values in `OnGetAsync` via `IGetTargeting`.

Keep SuperAdmin authorisation, the admin rail shell, and `ViewData["AdminTab"]` exactly as they are on that page. A 404 for an unknown customer id must still be a 404, not an exception.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: ALL PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Web/Areas/Admin/Pages/Customers/ tests/Coldstart.Web.Tests/CustomerDetailPageTests.cs
git commit -m "feat(admin): operator edits any customer's targeting from the detail page"
```

---

## Task 8: Verification pass

- [ ] **Step 1: Full build and suite**

Run: `dotnet build && dotnet test`
Expected: build clean, ALL PASS. Note that `dotnet build -warnaserror` fails on three pre-existing NU1510 package-pruning warnings in `Coldstart.Infrastructure.csproj` that have nothing to do with this work — use the plain build.

- [ ] **Step 2: Confirm the compatibility guarantee holds**

Run: `dotnet test tests/Coldstart.Application.Tests --filter "HarvestTargetingCompatibility"`
Expected: PASS. This is the evidence that sub-project A changed no customer's lead flow. If it fails, do not ship — the whole premise of A is that it is behaviour-neutral.

- [ ] **Step 3: Report what shipped**

State plainly in the handover that A adds the model and both editors but no lead-quality change, and that sub-projects B (query generation) and C (qualification) are what convert this structure into better leads.

## Self-Review Notes

- **Spec coverage:** the model (T1), storage (T2), shared read/write use cases (T3), compatibility and fallback (T4), generation with never-persist-silently and null-on-failure (T5), customer settings surface (T6), admin surface (T7), verification (T8). Error handling from the spec: generation failure keeps stored values (T5/T6), empty targeting valid (T1/T4), size-band and list normalisation (T1/T3), admin/customer share one path (T3/T7).
- **Type consistency:** `Targeting.Create(industries, geographies, buyerRoles, sizeMin, sizeMax, triggerEvents, exclusions)` (T1) is called with that exact argument order in T3 and asserted in T4. `TargetingInput` (T3) is produced by the parser (T5) and consumed by both pages (T6, T7). `HarvestTargeting.FromCustomer(profile, targeting, customerName)` (T4) matches its call site change in the same task.
- **Deliberate deviation from the spec's wording:** the spec described the lists as `IReadOnlyList<string>`. They are stored as newline-joined `string?` with computed `…List` accessors, because that matches `CompanyProfile`'s existing convention and avoids EF value converters. The domain API still takes and returns lists via `Create` and the accessors, so the spec's intent holds.
- **Known judgment calls for the implementer:** the exact `HarvestLeads` call site to change (T4 step 4 — only the per-customer path), the Groq generator's prompt wording (T5 step 4), and whether an existing test counts rail items (T6 step 4).

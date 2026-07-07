# Lead Review Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn `/app` into a working lead-review surface — New/Accepted/Discarded tabs, a per-lead detail page for editing and acting, drag-to-move between tabs, and research links to find a recipient.

**Architecture:** Hexagonal. New `Lead` domain transitions; a single tenant-checked inbound port `ILeadWorkflow` for all writes; read models (`IListLeads` returns all statuses, new `IGetLeadDetail` with computed `ContactLinkBuilder` links); Razor Pages for the tabbed index and detail page; one JSON `Move` handler + progressive-enhancement JS for drag. No schema/migration change — reuses the existing `LeadStatus` enum.

**Tech Stack:** .NET 10, ASP.NET Core Razor Pages, EF Core + Postgres, xUnit + NSubstitute + FluentAssertions, Testcontainers.

## Global Constraints

- Domain project has zero dependencies on other projects; no EF/ASP.NET in Domain (CS-HEX-01).
- Port interfaces live in Application, not Infrastructure (CS-HEX-02).
- DI wired only in composition roots (`AddApplication` / `AddInfrastructure` / Web `Program.cs`) (CS-HEX-03).
- Value Objects are `record`; entities use `private set` + factory/behaviour, no raw setters (CS-DDD-01/02).
- No `IQueryable<T>` crosses Infrastructure→Application; repos return materialized `IReadOnlyList<T>` (CS-DATA-01).
- Async all the way; `CancellationToken` from entry point to IO; no `.Result`/`.Wait()` (CS-DATA-03).
- Implementation classes `internal`; DTOs/VOs/ports `public` (CS-MOD-01).
- Tests: `Method_State_Expected` naming, strict `// Arrange`/`// Act`/`// Assert`, only mock outbound ports, ≥1 happy + 2 sad/edge per class.
- **No `Co-Authored-By` trailers on commits.**
- Build must stay at **0 warnings**; full suite green after every task.
- `Shelved` = operator-discarded; `Rejected` = engine auto-skip (FIT:NO). A drag never yields `Rejected` or `Sent`.

---

### Task 1: Domain transitions on `Lead`

**Files:**
- Modify: `src/Coldstart.Domain/Lead.cs`
- Test: `tests/Coldstart.Domain.Tests/LeadWorkflowTests.cs` (create)

**Interfaces:**
- Consumes: `LeadStatus` (`PendingReview, Approved, Rejected, Sent, Shelved`), `EmailAddress`, `DomainException`.
- Produces:
  - `void Approve(EmailAddress? recipient = null)` — optional recipient; throws if `Sent`.
  - `void SetRecipient(EmailAddress recipient)` — throws if `Sent`.
  - `void ReviseDraft(string subject, string draft)` — throws if `Sent`.
  - `void Discard()` — sets `Shelved`; throws if `Sent`.
  - `void Reopen()` — sets `PendingReview`; throws if `Sent`.
  - `MarkSent(DateTimeOffset)` unchanged (requires `Approved` + recipient).

- [ ] **Step 1: Write the failing tests**

Create `tests/Coldstart.Domain.Tests/LeadWorkflowTests.cs`:

```csharp
using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class LeadWorkflowTests
{
    private static Lead Harvested() => Lead.CreateHarvested(
        CustomerId.New(), "Acme", Fit.Yes, "subj", "body",
        "hackernews", "https://x/1", "fp-1", 0.7, "outbound", DateTimeOffset.UtcNow);

    [Fact]
    public void Approve_WithoutRecipient_ApprovesAndLeavesRecipientNull()
    {
        // Arrange
        var lead = Harvested();

        // Act
        lead.Approve();

        // Assert
        lead.Status.Should().Be(LeadStatus.Approved);
        lead.RecipientEmail.Should().BeNull();
    }

    [Fact]
    public void Approve_FromShelved_RescuesToApproved()
    {
        // Arrange
        var lead = Harvested();
        lead.Discard();

        // Act
        lead.Approve(new EmailAddress("ceo@acme.com"));

        // Assert
        lead.Status.Should().Be(LeadStatus.Approved);
        lead.RecipientEmail!.Value.Should().Be("ceo@acme.com");
    }

    [Fact]
    public void Discard_SetsShelved_NotRejected()
    {
        // Arrange
        var lead = Harvested();

        // Act
        lead.Discard();

        // Assert
        lead.Status.Should().Be(LeadStatus.Shelved);
    }

    [Fact]
    public void Reopen_FromRejected_ReturnsToPendingReview()
    {
        // Arrange
        var lead = Harvested();
        lead.Reject();

        // Act
        lead.Reopen();

        // Assert
        lead.Status.Should().Be(LeadStatus.PendingReview);
    }

    [Fact]
    public void MarkSent_WithoutRecipient_Throws()
    {
        // Arrange
        var lead = Harvested();
        lead.Approve();

        // Act
        var act = () => lead.MarkSent(DateTimeOffset.UtcNow);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ReviseDraft_AfterSent_Throws()
    {
        // Arrange
        var lead = Harvested();
        lead.Approve(new EmailAddress("ceo@acme.com"));
        lead.MarkSent(DateTimeOffset.UtcNow);

        // Act
        var act = () => lead.ReviseDraft("new", "new body");

        // Assert
        act.Should().Throw<DomainException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Coldstart.Domain.Tests/Coldstart.Domain.Tests.csproj`
Expected: FAIL — `Approve` overload / `Discard` / `Reopen` / `ReviseDraft` not found (compile error).

- [ ] **Step 3: Implement the transitions**

In `src/Coldstart.Domain/Lead.cs`, replace the existing `Approve` method and add the new methods (keep `Reject`, `Shelve`, `MarkSent`):

```csharp
public void Approve(EmailAddress? recipient = null)
{
    if (Status == LeadStatus.Sent)
        throw new DomainException("Cannot change a lead that has already been sent.");
    if (recipient is not null) RecipientEmail = recipient;
    Status = LeadStatus.Approved;
}

public void SetRecipient(EmailAddress recipient)
{
    if (Status == LeadStatus.Sent)
        throw new DomainException("Cannot change a lead that has already been sent.");
    RecipientEmail = recipient ?? throw new DomainException("Recipient is required.");
}

public void ReviseDraft(string subject, string draft)
{
    if (Status == LeadStatus.Sent)
        throw new DomainException("Cannot edit a lead that has already been sent.");
    Subject = subject ?? "";
    Draft = draft ?? "";
}

public void Discard()
{
    if (Status == LeadStatus.Sent)
        throw new DomainException("Cannot discard a lead that has already been sent.");
    Status = LeadStatus.Shelved;
}

public void Reopen()
{
    if (Status == LeadStatus.Sent)
        throw new DomainException("Cannot reopen a lead that has already been sent.");
    Status = LeadStatus.PendingReview;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Coldstart.Domain.Tests/Coldstart.Domain.Tests.csproj`
Expected: PASS (all, including existing).

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Domain/Lead.cs tests/Coldstart.Domain.Tests/LeadWorkflowTests.cs
git commit -m "feat(domain): lead review transitions (optional-recipient approve, revise, discard, reopen)"
```

---

### Task 2: Repository `GetAsync` / `UpdateAsync`

**Files:**
- Modify: `src/Coldstart.Application/Ports/ILeadRepository.cs`
- Modify: `src/Coldstart.Infrastructure/Repositories/LeadRepository.cs`
- Test: `tests/Coldstart.Infrastructure.Tests/LeadRepositoryTests.cs` (add cases)

**Interfaces:**
- Consumes: `Lead`, `LeadId`, `ColdstartDbContext.Leads`.
- Produces on `ILeadRepository`:
  - `Task<Lead?> GetAsync(LeadId id, CancellationToken ct)`
  - `Task UpdateAsync(Lead lead, CancellationToken ct)`

- [ ] **Step 1: Write the failing test**

Add to `tests/Coldstart.Infrastructure.Tests/LeadRepositoryTests.cs` (follow the existing fixture/customer-seeding pattern already in that file for creating a customer + lead):

```csharp
[Fact]
public async Task GetAsync_ThenUpdate_PersistsStatusChange()
{
    // Arrange
    await using var db = fixture.NewDbContext();
    var customer = Customer.Create("repo-update@test.com");
    await new CustomerRepository(db).AddAsync(customer, CancellationToken.None);
    var lead = Lead.CreateHarvested(customer.Id, "Acme", Fit.Yes, "s", "b",
        "hackernews", "https://x/1", "fp-upd", 0.7, "outbound", DateTimeOffset.UtcNow);
    var repo = new LeadRepository(db);
    await repo.AddAsync(lead, CancellationToken.None);

    // Act
    var loaded = await repo.GetAsync(lead.Id, CancellationToken.None);
    loaded!.Approve(new EmailAddress("ceo@acme.com"));
    await repo.UpdateAsync(loaded, CancellationToken.None);

    // Assert
    await using var read = fixture.NewDbContext();
    var reloaded = await new LeadRepository(read).GetAsync(lead.Id, CancellationToken.None);
    reloaded!.Status.Should().Be(LeadStatus.Approved);
    reloaded.RecipientEmail!.Value.Should().Be("ceo@acme.com");
}

[Fact]
public async Task GetAsync_WhenMissing_ReturnsNull()
{
    // Arrange
    await using var db = fixture.NewDbContext();
    var repo = new LeadRepository(db);

    // Act
    var loaded = await repo.GetAsync(LeadId.New(), CancellationToken.None);

    // Assert
    loaded.Should().BeNull();
}
```

(Match the existing `using`s / fixture field name in the file — it uses `PostgresFixture`.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests/Coldstart.Infrastructure.Tests.csproj --filter LeadRepositoryTests`
Expected: FAIL — `GetAsync`/`UpdateAsync` not defined on `ILeadRepository`.

- [ ] **Step 3: Add to the port**

In `src/Coldstart.Application/Ports/ILeadRepository.cs`, add inside the interface:

```csharp
    Task<Lead?> GetAsync(LeadId id, CancellationToken ct);
    Task UpdateAsync(Lead lead, CancellationToken ct);
```

- [ ] **Step 4: Implement in the repository**

In `src/Coldstart.Infrastructure/Repositories/LeadRepository.cs`, add:

```csharp
    public async Task<Lead?> GetAsync(LeadId id, CancellationToken ct) =>
        await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task UpdateAsync(Lead lead, CancellationToken ct)
    {
        db.Leads.Update(lead);
        await db.SaveChangesAsync(ct);
    }
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests/Coldstart.Infrastructure.Tests.csproj --filter LeadRepositoryTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Application/Ports/ILeadRepository.cs src/Coldstart.Infrastructure/Repositories/LeadRepository.cs tests/Coldstart.Infrastructure.Tests/LeadRepositoryTests.cs
git commit -m "feat(infra): ILeadRepository GetAsync/UpdateAsync"
```

---

### Task 3: `ILeadWorkflow` + tenant isolation

**Files:**
- Create: `src/Coldstart.Application/Leads/LeadActionResult.cs`
- Create: `src/Coldstart.Application/Leads/ILeadWorkflow.cs`
- Create: `src/Coldstart.Application/Leads/LeadWorkflow.cs`
- Modify: `src/Coldstart.Application/DependencyInjection.cs`
- Test: `tests/Coldstart.Application.Tests/LeadWorkflowTests.cs` (create)

**Interfaces:**
- Consumes: `ILeadRepository.GetAsync/UpdateAsync` (Task 2), `Lead` transitions (Task 1), `EmailAddress`, `DomainException`.
- Produces:
  - `enum LeadActionStatus { Ok, NotFound, Invalid }`
  - `record LeadActionResult(LeadActionStatus Status, string? Message = null)` with `LeadActionResult.Ok`, `LeadActionResult.NotFound`, `LeadActionResult.Invalid(string)`, `bool IsOk`.
  - `interface ILeadWorkflow` with `SaveDraftAsync/AcceptAsync(Guid customerId, Guid leadId, string subject, string draft, string? recipient, CancellationToken)`, `DiscardAsync/ReopenAsync/MarkSentAsync(Guid customerId, Guid leadId, CancellationToken)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Coldstart.Application.Tests/LeadWorkflowTests.cs`:

```csharp
using Coldstart.Application.Leads;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class LeadWorkflowTests
{
    private static Lead LeadFor(CustomerId customerId) => Lead.CreateHarvested(
        customerId, "Acme", Fit.Yes, "subj", "body",
        "hackernews", "https://x/1", "fp-1", 0.7, "outbound", DateTimeOffset.UtcNow);

    [Fact]
    public async Task AcceptAsync_WithoutRecipient_ApprovesAndPersists()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var sut = new LeadWorkflow(repo);

        // Act
        var result = await sut.AcceptAsync(customerId, lead.Id.Value, "s2", "b2", null, CancellationToken.None);

        // Assert
        result.IsOk.Should().BeTrue();
        lead.Status.Should().Be(LeadStatus.Approved);
        await repo.Received(1).UpdateAsync(lead, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptAsync_ForAnotherCustomersLead_ReturnsNotFound_AndDoesNotMutate()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var attackerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(ownerId));
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var sut = new LeadWorkflow(repo);

        // Act
        var result = await sut.AcceptAsync(attackerId, lead.Id.Value, "s", "b", null, CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.NotFound);
        lead.Status.Should().Be(LeadStatus.PendingReview);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Lead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptAsync_WithInvalidRecipient_ReturnsInvalid_AndDoesNotMutate()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var sut = new LeadWorkflow(repo);

        // Act
        var result = await sut.AcceptAsync(customerId, lead.Id.Value, "s", "b", "not-an-email", CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.Invalid);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Lead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkSentAsync_WithoutRecipient_ReturnsInvalid()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        lead.Approve();
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var sut = new LeadWorkflow(repo);

        // Act
        var result = await sut.MarkSentAsync(customerId, lead.Id.Value, CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.Invalid);
    }

    [Fact]
    public async Task DiscardAsync_WhenMissing_ReturnsNotFound()
    {
        // Arrange
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns((Lead?)null);
        var sut = new LeadWorkflow(repo);

        // Act
        var result = await sut.DiscardAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.NotFound);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Application.Tests/Coldstart.Application.Tests.csproj --filter LeadWorkflowTests`
Expected: FAIL — `LeadWorkflow`/`ILeadWorkflow`/`LeadActionResult` not found.

- [ ] **Step 3: Create `LeadActionResult.cs`**

```csharp
namespace Coldstart.Application.Leads;

public enum LeadActionStatus { Ok, NotFound, Invalid }

public sealed record LeadActionResult(LeadActionStatus Status, string? Message = null)
{
    public static readonly LeadActionResult Ok = new(LeadActionStatus.Ok);
    public static readonly LeadActionResult NotFound = new(LeadActionStatus.NotFound);
    public static LeadActionResult Invalid(string message) => new(LeadActionStatus.Invalid, message);
    public bool IsOk => Status == LeadActionStatus.Ok;
}
```

- [ ] **Step 4: Create `ILeadWorkflow.cs`**

```csharp
namespace Coldstart.Application.Leads;

public interface ILeadWorkflow
{
    Task<LeadActionResult> SaveDraftAsync(Guid customerId, Guid leadId, string subject, string draft, string? recipient, CancellationToken ct);
    Task<LeadActionResult> AcceptAsync(Guid customerId, Guid leadId, string subject, string draft, string? recipient, CancellationToken ct);
    Task<LeadActionResult> DiscardAsync(Guid customerId, Guid leadId, CancellationToken ct);
    Task<LeadActionResult> ReopenAsync(Guid customerId, Guid leadId, CancellationToken ct);
    Task<LeadActionResult> MarkSentAsync(Guid customerId, Guid leadId, CancellationToken ct);
}
```

- [ ] **Step 5: Create `LeadWorkflow.cs`**

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

internal sealed class LeadWorkflow(ILeadRepository leads) : ILeadWorkflow
{
    public Task<LeadActionResult> SaveDraftAsync(Guid customerId, Guid leadId, string subject, string draft, string? recipient, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead =>
        {
            lead.ReviseDraft(subject, draft);
            return ApplyRecipient(lead, recipient);
        });

    public Task<LeadActionResult> AcceptAsync(Guid customerId, Guid leadId, string subject, string draft, string? recipient, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead =>
        {
            lead.ReviseDraft(subject, draft);
            var r = ApplyRecipient(lead, recipient);
            if (!r.IsOk) return r;
            lead.Approve();
            return LeadActionResult.Ok;
        });

    public Task<LeadActionResult> DiscardAsync(Guid customerId, Guid leadId, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead => { lead.Discard(); return LeadActionResult.Ok; });

    public Task<LeadActionResult> ReopenAsync(Guid customerId, Guid leadId, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead => { lead.Reopen(); return LeadActionResult.Ok; });

    public Task<LeadActionResult> MarkSentAsync(Guid customerId, Guid leadId, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead =>
        {
            if (lead.RecipientEmail is null)
                return LeadActionResult.Invalid("A recipient is required before marking as sent.");
            lead.MarkSent(DateTimeOffset.UtcNow);
            return LeadActionResult.Ok;
        });

    // Empty/whitespace recipient is allowed (optional); a non-empty invalid one is rejected.
    private static LeadActionResult ApplyRecipient(Lead lead, string? recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient)) return LeadActionResult.Ok;
        EmailAddress email;
        try { email = new EmailAddress(recipient); }
        catch (DomainException ex) { return LeadActionResult.Invalid(ex.Message); }
        lead.SetRecipient(email);
        return LeadActionResult.Ok;
    }

    // Single tenant-isolation choke point: load, verify ownership, mutate, persist.
    private async Task<LeadActionResult> MutateAsync(Guid customerId, Guid leadId, CancellationToken ct, Func<Lead, LeadActionResult> mutate)
    {
        var lead = await leads.GetAsync(new LeadId(leadId), ct);
        if (lead is null || lead.CustomerId.Value != customerId)
            return LeadActionResult.NotFound;

        LeadActionResult result;
        try { result = mutate(lead); }
        catch (DomainException ex) { return LeadActionResult.Invalid(ex.Message); }
        if (!result.IsOk) return result;

        await leads.UpdateAsync(lead, ct);
        return LeadActionResult.Ok;
    }
}
```

- [ ] **Step 6: Register in DI**

In `src/Coldstart.Application/DependencyInjection.cs`, add after the `IGetLeadInbox` line:

```csharp
        services.AddScoped<ILeadWorkflow, LeadWorkflow>();
```

- [ ] **Step 7: Run to verify pass**

Run: `dotnet test tests/Coldstart.Application.Tests/Coldstart.Application.Tests.csproj --filter LeadWorkflowTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Coldstart.Application/Leads/LeadActionResult.cs src/Coldstart.Application/Leads/ILeadWorkflow.cs src/Coldstart.Application/Leads/LeadWorkflow.cs src/Coldstart.Application/DependencyInjection.cs tests/Coldstart.Application.Tests/LeadWorkflowTests.cs
git commit -m "feat(app): ILeadWorkflow with tenant-checked accept/discard/reopen/mark-sent"
```

---

### Task 4: Read models — unfiltered `ListLeads`, `IGetLeadDetail`, `ContactLinkBuilder`

**Files:**
- Modify: `src/Coldstart.Application/Leads/ListLeads.cs` (remove the Rejected/Shelved filter)
- Create: `src/Coldstart.Application/Leads/ContactLink.cs`
- Create: `src/Coldstart.Application/Leads/ContactLinkBuilder.cs`
- Create: `src/Coldstart.Application/Leads/LeadDetail.cs`
- Create: `src/Coldstart.Application/Leads/IGetLeadDetail.cs`
- Create: `src/Coldstart.Application/Leads/GetLeadDetail.cs`
- Modify: `src/Coldstart.Application/DependencyInjection.cs`
- Test: `tests/Coldstart.Application.Tests/ContactLinkBuilderTests.cs` (create)
- Test: `tests/Coldstart.Application.Tests/GetLeadDetailTests.cs` (create)
- Test: `tests/Coldstart.Application.Tests/ListLeadsTests.cs` (update — assert all statuses returned)

**Interfaces:**
- Consumes: `ILeadRepository.GetAsync/ForCustomerAsync`, `Lead`, `LeadId`.
- Produces:
  - `record ContactLink(string Label, string Url)`
  - `static class ContactLinkBuilder` → `IReadOnlyList<ContactLink> Build(string company, string sourceUrl, string buyerTitle = "Head of Growth")`
  - `record LeadDetail(Guid Id, string Company, string Fit, string Status, string Subject, string Draft, string? RecipientEmail, string Source, string SourceUrl, double Score, DateTimeOffset? SentAt, IReadOnlyList<ContactLink> ContactLinks)`
  - `interface IGetLeadDetail` → `Task<LeadDetail?> ForLeadAsync(Guid customerId, Guid leadId, CancellationToken ct)`

- [ ] **Step 1: Write the failing tests**

Create `tests/Coldstart.Application.Tests/ContactLinkBuilderTests.cs`:

```csharp
using Coldstart.Application.Leads;
using FluentAssertions;
using Xunit;

namespace Coldstart.Application.Tests;

public class ContactLinkBuilderTests
{
    [Fact]
    public void Build_WithCompanyAndSource_ReturnsEncodedResearchLinks()
    {
        // Arrange / Act
        var links = ContactLinkBuilder.Build("Acme Co", "https://x/1");

        // Assert
        links.Should().HaveCount(3);
        links[0].Url.Should().StartWith("https://www.linkedin.com/search/results/people/?keywords=");
        links[0].Url.Should().Contain("Acme%20Co");
        links[1].Url.Should().StartWith("https://www.google.com/search?q=");
        links[2].Should().BeEquivalentTo(new ContactLink("Signal source", "https://x/1"));
    }

    [Fact]
    public void Build_WithNoSourceUrl_OmitsSignalSource()
    {
        // Arrange / Act
        var links = ContactLinkBuilder.Build("Acme Co", "");

        // Assert
        links.Should().HaveCount(2);
    }

    [Fact]
    public void Build_WithUnusableCompany_ReturnsEmpty()
    {
        // Arrange / Act
        var dash = ContactLinkBuilder.Build("-", "https://x/1");
        var blank = ContactLinkBuilder.Build("   ", "https://x/1");

        // Assert
        dash.Should().BeEmpty();
        blank.Should().BeEmpty();
    }
}
```

Create `tests/Coldstart.Application.Tests/GetLeadDetailTests.cs`:

```csharp
using Coldstart.Application.Leads;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class GetLeadDetailTests
{
    private static Lead LeadFor(CustomerId customerId) => Lead.CreateHarvested(
        customerId, "Acme", Fit.Yes, "subj", "body",
        "hackernews", "https://x/1", "fp-1", 0.7, "outbound", DateTimeOffset.UtcNow);

    [Fact]
    public async Task ForLeadAsync_WhenOwned_ReturnsDetailWithContactLinks()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lead = LeadFor(new CustomerId(customerId));
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var sut = new GetLeadDetail(repo);

        // Act
        var detail = await sut.ForLeadAsync(customerId, lead.Id.Value, CancellationToken.None);

        // Assert
        detail.Should().NotBeNull();
        detail!.Company.Should().Be("Acme");
        detail.Subject.Should().Be("subj");
        detail.ContactLinks.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ForLeadAsync_ForAnotherCustomer_ReturnsNull()
    {
        // Arrange
        var lead = LeadFor(new CustomerId(Guid.NewGuid()));
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var sut = new GetLeadDetail(repo);

        // Act
        var detail = await sut.ForLeadAsync(Guid.NewGuid(), lead.Id.Value, CancellationToken.None);

        // Assert
        detail.Should().BeNull();
    }

    [Fact]
    public async Task ForLeadAsync_WhenMissing_ReturnsNull()
    {
        // Arrange
        var repo = Substitute.For<ILeadRepository>();
        repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns((Lead?)null);
        var sut = new GetLeadDetail(repo);

        // Act
        var detail = await sut.ForLeadAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        // Assert
        detail.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Coldstart.Application.Tests/Coldstart.Application.Tests.csproj --filter "ContactLinkBuilderTests|GetLeadDetailTests"`
Expected: FAIL — types not found.

- [ ] **Step 3: Create `ContactLink.cs`**

```csharp
namespace Coldstart.Application.Leads;

public sealed record ContactLink(string Label, string Url);
```

- [ ] **Step 4: Create `ContactLinkBuilder.cs`** (port of `leadengine/contact.py`)

```csharp
namespace Coldstart.Application.Leads;

public static class ContactLinkBuilder
{
    private const string DefaultBuyerTitle = "Head of Growth";
    private static readonly HashSet<string> Unusable = new(StringComparer.OrdinalIgnoreCase) { "-", "unknown", "n/a" };

    // Honest, actionable people-search links — never a fabricated address.
    public static IReadOnlyList<ContactLink> Build(string company, string sourceUrl, string buyerTitle = DefaultBuyerTitle)
    {
        company = (company ?? "").Trim();
        if (company.Length == 0 || Unusable.Contains(company)) return [];

        var links = new List<ContactLink>
        {
            new($"Find buyer ({buyerTitle})",
                "https://www.linkedin.com/search/results/people/?keywords=" + Uri.EscapeDataString($"{company} {buyerTitle}")),
            new("Web search",
                "https://www.google.com/search?q=" + Uri.EscapeDataString($"{company} {buyerTitle} LinkedIn")),
        };
        if (!string.IsNullOrWhiteSpace(sourceUrl))
            links.Add(new ContactLink("Signal source", sourceUrl));
        return links;
    }
}
```

- [ ] **Step 5: Create `LeadDetail.cs`**

```csharp
namespace Coldstart.Application.Leads;

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
    IReadOnlyList<ContactLink> ContactLinks);
```

- [ ] **Step 6: Create `IGetLeadDetail.cs`**

```csharp
namespace Coldstart.Application.Leads;

public interface IGetLeadDetail
{
    Task<LeadDetail?> ForLeadAsync(Guid customerId, Guid leadId, CancellationToken ct);
}
```

- [ ] **Step 7: Create `GetLeadDetail.cs`**

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

internal sealed class GetLeadDetail(ILeadRepository leads) : IGetLeadDetail
{
    public async Task<LeadDetail?> ForLeadAsync(Guid customerId, Guid leadId, CancellationToken ct)
    {
        var lead = await leads.GetAsync(new LeadId(leadId), ct);
        if (lead is null || lead.CustomerId.Value != customerId) return null;

        return new LeadDetail(
            lead.Id.Value, lead.Company, lead.Fit.ToString(), lead.Status.ToString(),
            lead.Subject, lead.Draft, lead.RecipientEmail?.Value,
            lead.Source, lead.SourceUrl, lead.Score, lead.SentAt,
            ContactLinkBuilder.Build(lead.Company, lead.SourceUrl));
    }
}
```

- [ ] **Step 8: Remove the inbox filter in `ListLeads.cs`**

In `src/Coldstart.Application/Leads/ListLeads.cs`, delete the `.Where(...)` clause and its comment so all statuses are returned:

```csharp
        var found = await leads.ForCustomerAsync(customerId, ct);
        return found
            .Select(l => new LeadSummary(
```

- [ ] **Step 9: Update `ListLeadsTests.cs`**

Replace the `ForCustomerAsync_ExcludesRejectedAndShelvedLeads` test (added earlier today) with one asserting the read model now returns every status (the tab routing is the page's job):

```csharp
    [Fact]
    public async Task ForCustomerAsync_ReturnsAllStatuses_IncludingRejectedAndShelved()
    {
        // Arrange
        var customerId = CustomerId.New();
        Lead Harvested(string company, string fp) => Lead.CreateHarvested(customerId, company, Fit.Yes,
            "subj", "body", "funding", "https://source.example/x", fp, 0.7, "sig", DateTimeOffset.UtcNow);
        var pending = Harvested("Pending Co", "fp-p");
        var rejected = Harvested("Rejected Co", "fp-r");
        rejected.Reject();
        var shelved = Harvested("Shelved Co", "fp-s");
        shelved.Discard();
        var repo = Substitute.For<ILeadRepository>();
        repo.ForCustomerAsync(customerId, Arg.Any<CancellationToken>())
            .Returns(new[] { pending, rejected, shelved });
        var sut = new ListLeads(repo);

        // Act
        var result = await sut.ForCustomerAsync(customerId, CancellationToken.None);

        // Assert — no filtering; the page routes each status to its tab
        result.Should().HaveCount(3);
        result.Select(r => r.Status).Should().Contain(new[] { "PendingReview", "Rejected", "Shelved" });
    }
```

- [ ] **Step 10: Register `IGetLeadDetail` in DI**

In `src/Coldstart.Application/DependencyInjection.cs`, add:

```csharp
        services.AddScoped<IGetLeadDetail, GetLeadDetail>();
```

- [ ] **Step 11: Run to verify pass**

Run: `dotnet test tests/Coldstart.Application.Tests/Coldstart.Application.Tests.csproj`
Expected: PASS.

- [ ] **Step 12: Commit**

```bash
git add src/Coldstart.Application/Leads/ContactLink.cs src/Coldstart.Application/Leads/ContactLinkBuilder.cs src/Coldstart.Application/Leads/LeadDetail.cs src/Coldstart.Application/Leads/IGetLeadDetail.cs src/Coldstart.Application/Leads/GetLeadDetail.cs src/Coldstart.Application/Leads/ListLeads.cs src/Coldstart.Application/DependencyInjection.cs tests/Coldstart.Application.Tests/
git commit -m "feat(app): lead detail read model + contact research links; ListLeads returns all statuses"
```

---

### Task 5: Tabbed `/app` index

**Files:**
- Modify: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml.cs`
- Modify: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml`
- Modify: `src/Coldstart.Web/wwwroot/css/site.css` (tab + card styles)
- Test: `tests/Coldstart.Web.Tests/LeadReviewTests.cs` (create)

**Interfaces:**
- Consumes: `IGetLeadInbox.ForCustomerAsync` (returns all statuses now), `IGetCompanyProfile`, `LeadSummary` (has `Id`, `Status`, `Company`, `Fit`, `Score`, `Subject`, `RecipientEmail`).
- Produces: `/app?tab=new|accepted|discarded` rendering; cards carry `data-lead-id` and link to `/app/leads/{id}`.

- [ ] **Step 1: Write the failing test**

Create `tests/Coldstart.Web.Tests/LeadReviewTests.cs`:

```csharp
using System.Net;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class LeadReviewTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Index_DefaultTab_ShowsNewLeadsAndTabHeaders()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"review-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(client, email, "Passw0rd!");
        await AddLeadForUserAsync(email, "Pending Co", "fp-" + Guid.NewGuid().ToString("N"), LeadStatus.PendingReview);

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("Pending Co");
        html.Should().Contain("app-tabs");
    }

    [Fact]
    public async Task Index_DiscardedTab_ShowsShelvedAndAutoSkippedGroups()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"review2-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(client, email, "Passw0rd!");
        await AddLeadForUserAsync(email, "Shelved Co", "fp-" + Guid.NewGuid().ToString("N"), LeadStatus.Shelved);
        await AddLeadForUserAsync(email, "Autoskip Co", "fp-" + Guid.NewGuid().ToString("N"), LeadStatus.Rejected);

        // Act
        var html = await client.GetStringAsync("/app?tab=discarded");

        // Assert
        html.Should().Contain("Shelved Co");
        html.Should().Contain("Autoskip Co");
        html.Should().Contain("Auto-skipped");
    }

    // Inserts a lead in a specific status for the given registered user's customer.
    private async Task AddLeadForUserAsync(string email, string company, string fingerprint, LeadStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", "https://x/1", fingerprint, 0.7, "outbound", DateTimeOffset.UtcNow);
        if (status == LeadStatus.Approved) lead.Approve(new EmailAddress("ceo@x.com"));
        else if (status == LeadStatus.Shelved) lead.Discard();
        else if (status == LeadStatus.Rejected) lead.Reject();
        else if (status == LeadStatus.Sent) { lead.Approve(new EmailAddress("ceo@x.com")); lead.MarkSent(DateTimeOffset.UtcNow); }
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter LeadReviewTests`
Expected: FAIL — no `app-tabs` markup / no `?tab` handling yet.

- [ ] **Step 3: Rewrite `Index.cshtml.cs`**

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
    public CompanyProfileView Profile { get; private set; } = new(null, null, null, null, null, null, null);

    public string Tab { get; private set; } = "new";
    public IReadOnlyList<LeadSummary> New { get; private set; } = [];
    public IReadOnlyList<LeadSummary> AcceptedReady { get; private set; } = [];
    public IReadOnlyList<LeadSummary> Sent { get; private set; } = [];
    public IReadOnlyList<LeadSummary> Shelved { get; private set; } = [];
    public IReadOnlyList<LeadSummary> AutoSkipped { get; private set; } = [];

    public int NewCount => New.Count;
    public int AcceptedCount => AcceptedReady.Count + Sent.Count;
    public int DiscardedCount => Shelved.Count + AutoSkipped.Count;

    public async Task OnGetAsync(string? tab, CancellationToken ct)
    {
        Tab = tab is "accepted" or "discarded" ? tab : "new";

        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return;

        var result = await inbox.ForCustomerAsync(user.CustomerId.Value, ct);
        CustomerName = result.CustomerName;
        Profile = await profiles.ForCustomerAsync(user.CustomerId.Value, ct);

        New = result.Leads.Where(l => l.Status == "PendingReview").ToList();
        AcceptedReady = result.Leads.Where(l => l.Status == "Approved").ToList();
        Sent = result.Leads.Where(l => l.Status == "Sent").ToList();
        Shelved = result.Leads.Where(l => l.Status == "Shelved").ToList();
        AutoSkipped = result.Leads.Where(l => l.Status == "Rejected").ToList();
    }
}
```

- [ ] **Step 4: Rewrite the leads section of `Index.cshtml`**

Keep the existing app-bar `<header>` and the `.app-profile` section unchanged. Replace everything from `<p class="app-sub">` / `<h1 class="app-leads-title">` down to the end of the old leads block with the tabbed markup below. Add a card partial by inlining the card. (`Model.Tab` selects which panel is visible; all leads still render server-side so counts are correct.)

```html
  <nav class="app-tabs" aria-label="Lead tabs">
    <a class="app-tab @(Model.Tab == "new" ? "is-active" : "")" data-tab="new" href="/app?tab=new">New <span class="app-tab-count">@Model.NewCount</span></a>
    <a class="app-tab @(Model.Tab == "accepted" ? "is-active" : "")" data-tab="accepted" href="/app?tab=accepted">Accepted <span class="app-tab-count">@Model.AcceptedCount</span></a>
    <a class="app-tab @(Model.Tab == "discarded" ? "is-active" : "")" data-tab="discarded" href="/app?tab=discarded">Discarded <span class="app-tab-count">@Model.DiscardedCount</span></a>
  </nav>

  @{
    void Card(Coldstart.Application.Leads.LeadSummary lead, bool draggable)
    {
      <article class="app-lead" data-lead-id="@lead.Id" draggable="@(draggable ? "true" : "false")">
        <div class="app-lead-top"><h3><a href="/app/leads/@lead.Id">@lead.Company</a></h3><span class="fit fit-@lead.Fit.ToLower()">@lead.Fit</span></div>
        <div class="app-lead-meta"><span class="chip">@lead.Source</span><span class="score">score @lead.Score.ToString("0.00")</span></div>
        <p class="app-lead-subj">@lead.Subject</p>
      </article>
    }
    void Cards(IReadOnlyList<Coldstart.Application.Leads.LeadSummary> leads, bool draggable, string emptyText)
    {
      if (leads.Count == 0) { <p class="app-group-empty">@emptyText</p> }
      else { <div class="app-leads">@foreach (var l in leads) { Card(l, draggable); }</div> }
    }
  }

  <section class="app-panel @(Model.Tab == "new" ? "is-active" : "")" data-panel="new">
    @{ Cards(Model.New, true, "No new leads — your next harvest will fill this in."); }
  </section>

  <section class="app-panel @(Model.Tab == "accepted" ? "is-active" : "")" data-panel="accepted">
    <h2 class="app-group-head">Ready</h2>
    @{ Cards(Model.AcceptedReady, true, "Nothing accepted yet."); }
    <h2 class="app-group-head">Sent</h2>
    @{ Cards(Model.Sent, false, "Nothing sent yet."); }
  </section>

  <section class="app-panel @(Model.Tab == "discarded" ? "is-active" : "")" data-panel="discarded">
    <h2 class="app-group-head">You discarded</h2>
    @{ Cards(Model.Shelved, true, "You haven't discarded anything."); }
    <h2 class="app-group-head">Auto-skipped · FIT:NO</h2>
    @{ Cards(Model.AutoSkipped, true, "No auto-skipped leads."); }
  </section>
```

- [ ] **Step 5: Add tab/panel CSS**

Append to the dashboard-shell block in `src/Coldstart.Web/wwwroot/css/site.css`:

```css
  .app-tabs { display: flex; gap: 6px; border-bottom: 2px solid var(--line-2); margin: 24px 0 18px; }
  .app-tab { font-family: var(--mono); font-size: 0.76rem; text-transform: uppercase; letter-spacing: 0.05em; color: var(--ink-soft); text-decoration: none; padding: 10px 16px; border: 2px solid transparent; border-bottom: none; border-radius: 10px 10px 0 0; }
  .app-tab.is-active { color: var(--ink); background: var(--cream-2); border-color: var(--line-2); }
  .app-tab.drop-hover { background: var(--orange); color: #fff; }
  .app-tab-count { font-size: 0.7rem; opacity: 0.7; margin-left: 4px; }
  .app-panel { display: none; }
  .app-panel.is-active { display: block; }
  .app-group-head { font-size: 0.9rem; font-family: var(--mono); text-transform: uppercase; letter-spacing: 0.06em; color: var(--orange-d); margin: 18px 0 12px; }
  .app-group-empty { color: var(--ink-soft); font-size: 0.9rem; }
  .app-lead[draggable="true"] { cursor: grab; }
  .app-lead.dragging { opacity: 0.5; }
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter LeadReviewTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Coldstart.Web/Areas/App/Pages/Index.cshtml src/Coldstart.Web/Areas/App/Pages/Index.cshtml.cs src/Coldstart.Web/wwwroot/css/site.css tests/Coldstart.Web.Tests/LeadReviewTests.cs
git commit -m "feat(zone): New/Accepted/Discarded tabs with sub-groups"
```

---

### Task 6: Lead detail page + action handlers

**Files:**
- Create: `src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml`
- Create: `src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml.cs`
- Modify: `src/Coldstart.Web/wwwroot/css/site.css` (detail styles — optional, reuse `auth-shell`/`field`)
- Test: `tests/Coldstart.Web.Tests/LeadDetailTests.cs` (create)

**Interfaces:**
- Consumes: `IGetLeadDetail.ForLeadAsync`, `ILeadWorkflow.{SaveDraft,Accept,Discard,Reopen,MarkSent}Async`, `LeadActionResult`.
- Produces: page at route `/app/leads/{id:guid}`.

- [ ] **Step 1: Write the failing test**

Create `tests/Coldstart.Web.Tests/LeadDetailTests.cs`. Reuses `AddLeadForUserAsync` shape from Task 5 (duplicate the private helper here, or make it a shared internal static — duplicate is fine for a test) and the `AntiforgeryForm` helper pattern from `CustomerZoneTests`:

```csharp
using System.Net;
using Coldstart.Domain;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class LeadDetailTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Detail_ForOwnedLead_RendersDraftAndContactLinks()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"detail-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(client, email, "Passw0rd!");
        var id = await AddLeadForUserAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"));

        // Act
        var html = await client.GetStringAsync($"/app/leads/{id}");

        // Assert
        html.Should().Contain("Acme Co");
        html.Should().Contain("linkedin.com/search");
    }

    [Fact]
    public async Task Accept_PersistsApprovedStatus()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"detail2-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(client, email, "Passw0rd!");
        var id = await AddLeadForUserAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"));
        var form = await AntiforgeryForm(client, $"/app/leads/{id}", new()
        {
            ["Input.Subject"] = "s", ["Input.Draft"] = "b", ["Input.Recipient"] = "ceo@acme.com",
        });

        // Act
        var post = await client.PostAsync($"/app/leads/{id}?handler=Accept", new FormUrlEncodedContent(form));

        // Assert
        post.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
        var accepted = await client.GetStringAsync("/app?tab=accepted");
        accepted.Should().Contain("Acme Co");
    }

    [Fact]
    public async Task Detail_ForAnotherCustomersLead_RedirectsToApp()
    {
        // Arrange — owner creates a lead
        var owner = factory.CreateClient();
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(owner, ownerEmail, "Passw0rd!");
        var id = await AddLeadForUserAsync(ownerEmail, "Owner Co", "fp-" + Guid.NewGuid().ToString("N"));

        // attacker is a different logged-in customer
        var attacker = factory.CreateClient(new() { AllowAutoRedirect = false });
        await AuthFlowTests.RegisterAsync(attacker, $"attacker-{Guid.NewGuid():N}@test.com", "Passw0rd!");

        // Act
        var response = await attacker.GetAsync($"/app/leads/{id}");

        // Assert — never leaks another tenant's lead
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/app");
    }

    private async Task<Guid> AddLeadForUserAsync(string email, string company, string fingerprint)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", "https://x/1", fingerprint, 0.7, "outbound", DateTimeOffset.UtcNow);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id.Value;
    }

    private static async Task<Dictionary<string, string>> AntiforgeryForm(HttpClient client, string path, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(path);
        var marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start >= 0)
        {
            start += marker.Length;
            var end = html.IndexOf('"', start);
            fields["__RequestVerificationToken"] = html[start..end];
        }
        return fields;
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter LeadDetailTests`
Expected: FAIL — no `/app/leads/{id}` page.

- [ ] **Step 3: Create `Detail.cshtml.cs`**

```csharp
using Coldstart.Application.Leads;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages.Leads;

[Authorize]
public sealed class DetailModel(UserManager<ApplicationUser> users, IGetLeadDetail getDetail, ILeadWorkflow workflow) : PageModel
{
    public LeadDetail Lead { get; private set; } = null!;
    [BindProperty] public LeadInput Input { get; set; } = new();
    [TempData] public string? Notice { get; set; }

    public sealed class LeadInput
    {
        public string Subject { get; set; } = "";
        public string Draft { get; set; } = "";
        public string? Recipient { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var detail = await LoadAsync(id, ct);
        if (detail is null) return RedirectToPage("/Index", new { area = "App" });
        Input = new LeadInput { Subject = detail.Subject, Draft = detail.Draft, Recipient = detail.RecipientEmail };
        return Page();
    }

    public Task<IActionResult> OnPostSaveAsync(Guid id, CancellationToken ct) =>
        RunAsync(id, ct, cid => workflow.SaveDraftAsync(cid, id, Input.Subject, Input.Draft, Input.Recipient, ct));

    public Task<IActionResult> OnPostAcceptAsync(Guid id, CancellationToken ct) =>
        RunAsync(id, ct, cid => workflow.AcceptAsync(cid, id, Input.Subject, Input.Draft, Input.Recipient, ct));

    public Task<IActionResult> OnPostDiscardAsync(Guid id, CancellationToken ct) =>
        RunAsync(id, ct, cid => workflow.DiscardAsync(cid, id, ct));

    public Task<IActionResult> OnPostReopenAsync(Guid id, CancellationToken ct) =>
        RunAsync(id, ct, cid => workflow.ReopenAsync(cid, id, ct));

    public Task<IActionResult> OnPostMarkSentAsync(Guid id, CancellationToken ct) =>
        RunAsync(id, ct, cid => workflow.MarkSentAsync(cid, id, ct));

    private async Task<LeadDetail?> LoadAsync(Guid id, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return null;
        var detail = await getDetail.ForLeadAsync(user.CustomerId.Value, id, ct);
        if (detail is not null) Lead = detail;
        return detail;
    }

    private async Task<IActionResult> RunAsync(Guid id, CancellationToken ct, Func<Guid, Task<LeadActionResult>> action)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return RedirectToPage("/Index", new { area = "App" });

        var result = await action(user.CustomerId.Value);
        if (result.Status == LeadActionStatus.NotFound)
            return RedirectToPage("/Index", new { area = "App" });
        if (result.Status == LeadActionStatus.Invalid)
        {
            // Re-render the page with the message and the loaded lead.
            if (await LoadAsync(id, ct) is null) return RedirectToPage("/Index", new { area = "App" });
            ModelState.AddModelError(string.Empty, result.Message ?? "That action isn't allowed.");
            return Page();
        }
        return RedirectToPage("/Index", new { area = "App" });
    }
}
```

- [ ] **Step 4: Create `Detail.cshtml`**

```html
@page "/app/leads/{id:guid}"
@model Coldstart.Web.Areas.App.Pages.Leads.DetailModel
@{ ViewData["Title"] = "Lead"; Layout = "/Pages/Shared/_Layout.cshtml"; var lead = Model.Lead; var sent = lead.Status == "Sent"; }

<main class="wrap auth-shell">
  <div class="auth-card app-detail">
    <p class="auth-alt"><a href="/app">← Back to your leads</a></p>
    <div class="app-lead-top"><h1>@lead.Company</h1><span class="fit fit-@lead.Fit.ToLower()">@lead.Fit</span></div>
    <div class="app-lead-meta"><span class="chip">@lead.Source</span><span class="score">score @lead.Score.ToString("0.00")</span><span class="status">@lead.Status</span></div>
    @if (!string.IsNullOrEmpty(lead.SourceUrl)) { <p><a href="@lead.SourceUrl" target="_blank" rel="noopener">View signal source</a></p> }

    <div asp-validation-summary="All" class="field-error"></div>

    <form method="post">
      <div class="field"><label asp-for="Input.Subject">Subject</label><input asp-for="Input.Subject" @(sent ? "disabled" : "")></div>
      <div class="field"><label asp-for="Input.Draft">Draft</label><textarea asp-for="Input.Draft" rows="8" @(sent ? "disabled" : "")></textarea></div>
      <div class="field"><label asp-for="Input.Recipient">Recipient email</label><input asp-for="Input.Recipient" type="email" @(sent ? "disabled" : "")></div>

      @if (!sent)
      {
        <div class="app-detail-actions">
          <button class="btn btn-ghost" type="submit" asp-page-handler="Save">Save draft</button>
          <button class="btn btn-primary" type="submit" asp-page-handler="Accept">Accept</button>
          <button class="btn btn-ghost" type="submit" asp-page-handler="MarkSent">Mark as sent</button>
          <button class="btn btn-ghost" type="submit" asp-page-handler="Discard">Discard</button>
          <button class="btn btn-ghost" type="submit" asp-page-handler="Reopen">Move to New</button>
        </div>
      }
      else { <p class="app-sub">This lead was sent@(lead.SentAt is null ? "" : $" on {lead.SentAt:yyyy-MM-dd}") and is read-only.</p> }
    </form>

    @if (lead.ContactLinks.Count > 0)
    {
      <div class="app-contact">
        <h2 class="app-group-head">Find the recipient</h2>
        <ul>@foreach (var link in lead.ContactLinks) { <li><a href="@link.Url" target="_blank" rel="noopener">@link.Label</a></li> }</ul>
      </div>
    }
  </div>
</main>
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter LeadDetailTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Web/Areas/App/Pages/Leads/ tests/Coldstart.Web.Tests/LeadDetailTests.cs
git commit -m "feat(zone): lead detail page with edit + accept/discard/reopen/mark-sent + research links"
```

---

### Task 7: Drag-to-move endpoint + JS

**Files:**
- Modify: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml.cs` (add `OnPostMoveAsync`)
- Modify: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml` (emit antiforgery token + script include)
- Modify: `src/Coldstart.Web/Program.cs` (antiforgery header name)
- Create: `src/Coldstart.Web/wwwroot/js/app-leads.js`
- Test: `tests/Coldstart.Web.Tests/LeadReviewTests.cs` (add Move cases)

**Interfaces:**
- Consumes: `ILeadWorkflow.{Reopen,Accept,Discard}Async`, `LeadActionResult`.
- Produces: `POST /app?handler=Move` (page handler `OnPostMoveAsync(Guid id, string tab, CancellationToken)`) → `JsonResult { ok, status, message }`.

- [ ] **Step 1: Write the failing test**

Add to `tests/Coldstart.Web.Tests/LeadReviewTests.cs` (uses the `AddLeadForUserAsync` already in that class; add an antiforgery helper as in `CustomerZoneTests`):

```csharp
    [Fact]
    public async Task Move_ToDiscarded_ShelvesLeadAndReturnsOk()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"move-{Guid.NewGuid():N}@test.com";
        await AuthFlowTests.RegisterAsync(client, email, "Passw0rd!");
        var id = await AddLeadIdForUserAsync(email, "Move Co", "fp-" + Guid.NewGuid().ToString("N"));
        var token = await AntiforgeryToken(client, "/app");

        // Act
        var req = new HttpRequestMessage(HttpMethod.Post, "/app?handler=Move");
        req.Headers.Add("RequestVerificationToken", token);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = id.ToString(), ["tab"] = "discarded" });
        var resp = await client.SendAsync(req);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("\"ok\":true");
        var discarded = await client.GetStringAsync("/app?tab=discarded");
        discarded.Should().Contain("Move Co");
    }

    // Returns the created lead's id (PendingReview).
    private async Task<Guid> AddLeadIdForUserAsync(string email, string company, string fingerprint)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        var lead = Lead.CreateHarvested(new CustomerId(user!.CustomerId!.Value), company, Fit.Yes,
            "subj", "body", "hackernews", "https://x/1", fingerprint, 0.7, "outbound", DateTimeOffset.UtcNow);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id.Value;
    }

    private static async Task<string> AntiforgeryToken(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = html.IndexOf('"', start);
        return html[start..end];
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter LeadReviewTests`
Expected: FAIL — no `Move` handler / antiforgery header not configured (400 or 404).

- [ ] **Step 3: Configure the antiforgery header name**

In `src/Coldstart.Web/Program.cs`, add near the other service registrations (before `builder.Build()`):

```csharp
builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");
```

- [ ] **Step 4: Add the `Move` handler + `ILeadWorkflow` to `IndexModel`**

In `Index.cshtml.cs`, add `ILeadWorkflow workflow` to the primary constructor parameters:

```csharp
public sealed class IndexModel(UserManager<ApplicationUser> users, IGetLeadInbox inbox, IGetCompanyProfile profiles, ILeadWorkflow workflow) : PageModel
```

Add these usings at the top: `using Microsoft.AspNetCore.Mvc;`. Then add the handler:

```csharp
    public async Task<IActionResult> OnPostMoveAsync(Guid id, string tab, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return new JsonResult(new { ok = false, message = "Not signed in." });
        var cid = user.CustomerId.Value;

        var result = tab switch
        {
            "new" => await workflow.ReopenAsync(cid, id, ct),
            "accepted" => await workflow.AcceptAsync(cid, id, /*subject*/ "", /*draft*/ "", /*recipient*/ null, ct),
            "discarded" => await workflow.DiscardAsync(cid, id, ct),
            _ => LeadActionResult.Invalid("Unknown tab."),
        };
        return new JsonResult(new { ok = result.IsOk, status = result.Status.ToString(), message = result.Message });
    }
```

**Note:** the `accepted` branch passes empty subject/draft — that would blank the draft via `ReviseDraft`. To avoid clobbering, add a dedicated verb. Replace `AcceptAsync` here with a status-only approve: extend `ILeadWorkflow` with `Task<LeadActionResult> ApproveAsync(Guid customerId, Guid leadId, CancellationToken ct)` that calls only `lead.Approve()` (no draft revise). Implement it in `LeadWorkflow`:

```csharp
    public Task<LeadActionResult> ApproveAsync(Guid customerId, Guid leadId, CancellationToken ct) =>
        MutateAsync(customerId, leadId, ct, lead => { lead.Approve(); return LeadActionResult.Ok; });
```

Add it to the interface, and use `workflow.ApproveAsync(cid, id, ct)` in the `"accepted"` branch. (Add one Application test: `ApproveAsync_KeepsExistingDraft`.)

- [ ] **Step 5: Emit the antiforgery token + script in `Index.cshtml`**

At the top of the leads area (inside `<main>`, before the tabs), add a hidden token and a script include at the end of the page:

```html
@Html.AntiForgeryToken()
```

At the very end of `Index.cshtml`:

```html
<script src="~/js/app-leads.js" defer></script>
```

- [ ] **Step 6: Create `app-leads.js`**

```javascript
// Progressive enhancement: drag a lead card onto a tab header to move its state.
(function () {
  var dragId = null;

  document.querySelectorAll('.app-lead[draggable="true"]').forEach(function (card) {
    card.addEventListener('dragstart', function (e) {
      dragId = card.getAttribute('data-lead-id');
      card.classList.add('dragging');
      e.dataTransfer.effectAllowed = 'move';
    });
    card.addEventListener('dragend', function () { card.classList.remove('dragging'); });
  });

  var tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
  var token = tokenEl ? tokenEl.value : '';

  document.querySelectorAll('.app-tab').forEach(function (tab) {
    tab.addEventListener('dragover', function (e) { e.preventDefault(); tab.classList.add('drop-hover'); });
    tab.addEventListener('dragleave', function () { tab.classList.remove('drop-hover'); });
    tab.addEventListener('drop', function (e) {
      e.preventDefault();
      tab.classList.remove('drop-hover');
      if (!dragId) return;
      var target = tab.getAttribute('data-tab');
      var body = new URLSearchParams();
      body.set('id', dragId);
      body.set('tab', target);
      fetch('/app?handler=Move', {
        method: 'POST',
        headers: { 'RequestVerificationToken': token, 'Content-Type': 'application/x-www-form-urlencoded' },
        body: body.toString()
      }).then(function (r) { return r.json(); }).then(function (res) {
        if (res.ok) { window.location = '/app?tab=' + target; }
        else { alert(res.message || 'Could not move that lead.'); }
      }).catch(function () { alert('Could not move that lead.'); });
      dragId = null;
    });
  });
})();
```

- [ ] **Step 7: Run to verify pass**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter LeadReviewTests`
Expected: PASS.

- [ ] **Step 8: Full suite + build**

Run: `dotnet build -clp:ErrorsOnly` then `dotnet test`
Expected: build 0 warnings; all tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/Coldstart.Web/Program.cs src/Coldstart.Web/Areas/App/Pages/Index.cshtml src/Coldstart.Web/Areas/App/Pages/Index.cshtml.cs src/Coldstart.Web/wwwroot/js/app-leads.js src/Coldstart.Application/Leads/ILeadWorkflow.cs src/Coldstart.Application/Leads/LeadWorkflow.cs tests/
git commit -m "feat(zone): drag-to-move cards between tabs (Move endpoint + JS, antiforgery header)"
```

---

## Self-Review

**Spec coverage:**
- Tabs New/Accepted/Discarded + sub-groups → Task 5. ✔
- Detail page edit + actions + research links → Task 6. ✔
- Accept/Discard/Reopen/MarkSent + recipient-optional → Tasks 1, 3, 6. ✔
- Drag onto tab headers, self-move no-op (via unknown/same handled by transitions), Sent non-draggable (`draggable="false"` on Sent cards) → Task 7 + Task 5. ✔
- Research links port of `contact.py` → Task 4. ✔
- Tenant isolation single choke point + adversarial test → Task 3 (`MutateAsync`), Tasks 4/6 read + web. ✔
- Supersede earlier `ListLeads` filter → Task 4 Step 8–9. ✔
- No schema change → confirmed (reuses `LeadStatus`). ✔

**Placeholder scan:** none — all steps carry real code/commands. The one design note in Task 7 Step 4 (empty-draft clobber) is resolved in the same step by adding `ApproveAsync`.

**Type consistency:** `ILeadWorkflow` gains `ApproveAsync` in Task 7 (interface + impl + usage updated together). `LeadActionResult`/`LeadActionStatus`, `LeadDetail`, `ContactLink`, `LeadSummary.Status` strings (`PendingReview/Approved/Sent/Shelved/Rejected`) are consistent across Tasks 3–7. `CustomerId.Value`, `LeadId(Guid)`, `EmailAddress(string)` match the domain.

**Notes for the implementer:**
- Web tests need Docker (Testcontainers Postgres), same as the existing suite.
- `Move` returns 200 JSON even on logical failure (`ok:false`); only auth/antiforgery yield non-200.
- If the anonymous-object JSON casing differs, assert on `ok` case-insensitively or configure camelCase — default `System.Text.Json` in `JsonResult` produces `"ok":true` (camelCase) as asserted.

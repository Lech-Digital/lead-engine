# Coldstart Frontend & Customer Zone — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the Coldstart marketing site into Razor, add a server-side contact-capture modal, wire ASP.NET Core Identity (email+password + Google, merge-by-email), and add an authorized customer zone that lists each user's seeded mock leads.

**Architecture:** All in `Coldstart.Web` over the SP1 hexagonal layers. New Domain aggregate `Inquiry`; new Application ports/use cases (`ISubmitInquiry`, `IProvisionCustomer`, `IEmailSender`, `IInquiryRepository`, and a `IGetLeadInbox.ForCustomerAsync(Guid)` overload); Infrastructure adapters (`InquiryRepository`, MailKit `SmtpEmailSender`, Identity via `ApplicationUser` + `ColdstartDbContext : IdentityDbContext`). DI is wired only in the Web composition root.

**Tech Stack:** .NET 10, Razor Pages, ASP.NET Core Identity + Google auth, EF Core 10 + Npgsql (Postgres 17), MailKit, xUnit + NSubstitute + FluentAssertions 7.*, Testcontainers.PostgreSql, Microsoft.AspNetCore.Mvc.Testing.

## Global Constraints

- **TFM:** `net10.0`. Build must stay at **0 warnings**.
- **Hexagonal (CS-HEX):** Domain zero deps; ports in Application; DI only in Web composition root. Web sees read-models (`LeadSummary`/`LeadInbox`) + Application request/response records, never Domain entities (Identity `UserManager`/`ApplicationUser` in Web UI is allowed — it's the composition/UI layer).
- **DDD (CS-DDD):** value objects are `record`; entities via factories with `private set`; behaviour not setters; `DomainException` on invalid state.
- **Data (CS-DATA):** repositories return `Task<IReadOnlyList<T>>` / `Task`, never `IQueryable`; async all the way; `CancellationToken` on every IO method.
- **Modifiers (CS-MOD):** implementation classes `internal`; `[InternalsVisibleTo]` for test projects only.
- **EF Core packages pinned to 10.0.4** (do not introduce a different version). Use the non-obsolete `new PostgreSqlBuilder("postgres:17")` Testcontainers ctor.
- **Testing:** xUnit + NSubstitute + FluentAssertions 7.*; naming `Method_State_Expected`; strict three-phase AAA comments; integration tests use real Postgres via Testcontainers + a fake `IEmailSender`; only mock outbound ports.
- **Secrets:** Gmail app password, Google client id/secret via user-secrets/env — never committed.
- **Brand parity:** the landing page must be visually 1:1 with the existing `site/index.html`.
- **Builds on SP1** (branch `dotnet-port`). Repo-root `NuGet.Config` is required (global config points at an unreachable proxy) — leave it. Docker must be running for integration tests.

---

## File Structure

```
src/Coldstart.Domain/
  Inquiry.cs  InquiryId.cs
src/Coldstart.Application/
  Ports/IInquiryRepository.cs  Ports/IEmailSender.cs
  Contact/ISubmitInquiry.cs  Contact/SubmitInquiry.cs  Contact/SubmitInquiryRequest.cs
  Customers/IProvisionCustomer.cs  Customers/ProvisionCustomer.cs
  Leads/IGetLeadInbox.cs (add ForCustomerAsync(Guid))  Leads/GetLeadInbox.cs (update)
  DependencyInjection.cs (register new use cases)
src/Coldstart.Infrastructure/
  Identity/ApplicationUser.cs
  ColdstartDbContext.cs (-> IdentityDbContext<ApplicationUser>)
  Configurations/InquiryConfiguration.cs
  Repositories/InquiryRepository.cs
  Email/SmtpEmailSender.cs  Email/SmtpOptions.cs
  DependencyInjection.cs (register InquiryRepository, SmtpEmailSender)
  Migrations/ (new: Inquiry + Identity + ApplicationUser.CustomerId)
src/Coldstart.Web/
  Program.cs (Identity + Google + auth pipeline)
  Pages/Index.cshtml(.cs)      # landing
  Pages/Privacy.cshtml(.cs)
  Pages/Shared/_Layout.cshtml
  Pages/Contact.cshtml.cs      # POST handler for the modal (or handler on Index)
  Areas/Identity/Pages/...     # register/login/logout (scaffolded, restyled)
  Areas/App/Pages/Index.cshtml(.cs)   # customer zone [Authorize]
  wwwroot/css/site.css  wwwroot/js/contact.js  wwwroot/{favicon.svg,logo.svg,site.webmanifest}
  (remove SP1 Pages/Leads.cshtml(.cs))
tests/Coldstart.Domain.Tests/InquiryTests.cs
tests/Coldstart.Application.Tests/{SubmitInquiryTests,ProvisionCustomerTests,GetLeadInboxTests(update)}.cs
tests/Coldstart.Infrastructure.Tests/InquiryRepositoryTests.cs
tests/Coldstart.Web.Tests/{ContactTests,AuthFlowTests,CustomerZoneTests,PublicPagesTests}.cs
  (remove SP1 LeadsPageTests.cs)
```

---

### Task 1: Brand port — shared layout, assets, landing page

**Files:**
- Create: `src/Coldstart.Web/Pages/Shared/_Layout.cshtml`, `wwwroot/css/site.css`, `wwwroot/js/contact.js`, copy `wwwroot/{favicon.svg,logo.svg,site.webmanifest}`
- Modify: `src/Coldstart.Web/Pages/Index.cshtml(.cs)`
- Remove: `src/Coldstart.Web/Pages/Leads.cshtml(.cs)`, `tests/Coldstart.Web.Tests/LeadsPageTests.cs`
- Test: `tests/Coldstart.Web.Tests/PublicPagesTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: a running landing page at `/` using the shared brand layout; the contact modal markup + `contact.js` (mailto behavior only for now — server POST added in Task 5). Removes the SP1 `/Leads` demo page.

- [ ] **Step 1: Port the brand assets and markup**

The source of truth is the existing `site/index.html` in the repo (721 lines, self-contained). Port it, preserving visual output exactly:
- Copy `site/favicon.svg`, `site/logo.svg`, `site/site.webmanifest` → `src/Coldstart.Web/wwwroot/`.
- Move the `<style>` block from `site/index.html` verbatim into `src/Coldstart.Web/wwwroot/css/site.css`.
- Move the modal `<script>` block into `src/Coldstart.Web/wwwroot/js/contact.js` (keep the mailto/preview logic as-is for now).
- Create `Pages/Shared/_Layout.cshtml` with the `<head>` (title block via `@ViewData["Title"]`, `<link rel="stylesheet" href="~/css/site.css">`, favicon/manifest links) and a `@RenderBody()` where page content goes; include `<script src="~/js/contact.js" defer></script>`.
- `Pages/Index.cshtml`: `@page`, set `Layout = "_Layout"`, and paste the `<body>` content of `site/index.html` (statusbar + hero + the five `<section>`s + the modal `<div class="overlay">`). Delete `Pages/Index.cshtml.cs`'s scaffold text; keep an empty `IndexModel`.

- [ ] **Step 2: Remove the SP1 demo Leads page + its test**

```bash
cd /Users/andrzej.lech/Code/private/lead-engine
rm src/Coldstart.Web/Pages/Leads.cshtml src/Coldstart.Web/Pages/Leads.cshtml.cs
rm tests/Coldstart.Web.Tests/LeadsPageTests.cs
```
(The customer zone replaces it in Task 8. `IGetLeadInbox.ForFirstCustomerAsync` becomes unused; it is replaced in Task 8 — leave it for now so the build stays green.)

- [ ] **Step 3: Write the failing public-pages test**

`tests/Coldstart.Web.Tests/PublicPagesTests.cs` (reuses the existing `ColdstartWebFactory` from SP1 — it wires a Testcontainers Postgres):

```csharp
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class PublicPagesTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task GetLanding_ReturnsOk_WithBrandAndCta()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        html.Should().Contain("Coldstart");
        html.Should().Contain("Send me an email");
    }
}
```

- [ ] **Step 4: Run to verify it fails, then build the page to pass**

Run: `dotnet test tests/Coldstart.Web.Tests --filter PublicPagesTests`
Expected: FAIL until Index is ported; PASS once `/` renders the brand + CTA. (Docker required.)

- [ ] **Step 5: Confirm build clean + commit**

Run: `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Web tests/Coldstart.Web.Tests
git commit -m "feat(web): port landing page + brand layout into Razor; remove SP1 demo page"
```

---

### Task 2: Privacy page

**Files:**
- Create: `src/Coldstart.Web/Pages/Privacy.cshtml(.cs)`
- Test: extend `tests/Coldstart.Web.Tests/PublicPagesTests.cs`

**Interfaces:**
- Consumes: `_Layout` (Task 1). Produces: `/privacy` page.

- [ ] **Step 1: Write the failing test (add to PublicPagesTests)**

```csharp
    [Fact]
    public async Task GetPrivacy_ReturnsOk_WithPolicyMarkers()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/privacy");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        html.Should().Contain("Privacy");
        html.Should().Contain("Schiedam");
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests --filter PublicPagesTests`
Expected: FAIL — `/privacy` 404.

- [ ] **Step 3: Create the privacy page**

`Pages/Privacy.cshtml` (`@page`, `Layout="_Layout"`, `ViewData["Title"]="Privacy"`). Content = a starter policy inside the brand container, with these sections: intro + a visible "Draft — pending review, not legal advice" note; **Who we are** (Coldstart, Schiedam, Netherlands; KvK: _[to be added]_, VAT: _[to be added]_); **What we collect** (contact-form inquiries: name, email, niche, what-you-sell, optional message; account email; authentication cookies); **Why** (respond to inquiries; provide the service); **Retention** (kept until you ask us to delete); **Your rights** (access/deletion by emailing us); **Contact** (andrzej.lech.andrzej@gmail.com). `Pages/Privacy.cshtml.cs`: empty `PrivacyModel : PageModel`.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Coldstart.Web.Tests --filter PublicPagesTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Web/Pages/Privacy.cshtml src/Coldstart.Web/Pages/Privacy.cshtml.cs tests/Coldstart.Web.Tests/PublicPagesTests.cs
git commit -m "feat(web): add brand-matched privacy page (starter draft)"
```

---

### Task 3: Inquiry domain + Application contact use case

**Files:**
- Create: `src/Coldstart.Domain/InquiryId.cs`, `Inquiry.cs`; `src/Coldstart.Application/Ports/IInquiryRepository.cs`, `Ports/IEmailSender.cs`, `Contact/SubmitInquiryRequest.cs`, `Contact/ISubmitInquiry.cs`, `Contact/SubmitInquiry.cs`
- Modify: `src/Coldstart.Application/DependencyInjection.cs`
- Test: `tests/Coldstart.Domain.Tests/InquiryTests.cs`, `tests/Coldstart.Application.Tests/SubmitInquiryTests.cs`

**Interfaces:**
- Consumes: `EmailAddress`, `DomainException` (SP1).
- Produces:
  - `record InquiryId(Guid Value) { static InquiryId New() }`
  - `Inquiry` with `static Inquiry Create(string name, EmailAddress email, string niche, string whatYouSell, string? message, DateTimeOffset createdAt)`; props `Id, Name, Email, Niche, WhatYouSell, Message, CreatedAt`
  - `interface IInquiryRepository { Task AddAsync(Inquiry inquiry, CancellationToken ct); }`
  - `interface IEmailSender { Task SendAsync(string to, string subject, string body, CancellationToken ct); }`
  - `record SubmitInquiryRequest(string Name, string Email, string Niche, string WhatYouSell, string? Message)`
  - `interface ISubmitInquiry { Task SubmitAsync(SubmitInquiryRequest request, CancellationToken ct); }`
  - `SubmitInquiry` impl; DI registration `AddApplication` adds `ISubmitInquiry`.

- [ ] **Step 1: Write the failing Inquiry domain test**

`tests/Coldstart.Domain.Tests/InquiryTests.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class InquiryTests
{
    [Fact]
    public void Create_WithValidFields_SetsProperties()
    {
        // Arrange
        var when = new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero);

        // Act
        var inquiry = Inquiry.Create("Sam", new EmailAddress("sam@acme.com"), "B2B SaaS", "scheduling", null, when);

        // Assert
        inquiry.Name.Should().Be("Sam");
        inquiry.Email.Value.Should().Be("sam@acme.com");
        inquiry.WhatYouSell.Should().Be("scheduling");
        inquiry.CreatedAt.Should().Be(when);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_ThrowsDomainException(string bad)
    {
        // Arrange
        var act = () => Inquiry.Create(bad, new EmailAddress("sam@acme.com"), "n", "s", null, DateTimeOffset.UtcNow);

        // Act & Assert
        act.Should().Throw<DomainException>();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Domain.Tests --filter InquiryTests`
Expected: FAIL — `Inquiry` not found.

- [ ] **Step 3: Implement Inquiry + InquiryId**

`src/Coldstart.Domain/InquiryId.cs`:

```csharp
namespace Coldstart.Domain;

public sealed record InquiryId(Guid Value)
{
    public static InquiryId New() => new(Guid.NewGuid());
}
```

`src/Coldstart.Domain/Inquiry.cs`:

```csharp
namespace Coldstart.Domain;

public sealed class Inquiry
{
    public InquiryId Id { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public EmailAddress Email { get; private set; } = null!;
    public string Niche { get; private set; } = string.Empty;
    public string WhatYouSell { get; private set; } = string.Empty;
    public string? Message { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Inquiry() { } // EF

    private Inquiry(InquiryId id, string name, EmailAddress email, string niche,
        string whatYouSell, string? message, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Email = email;
        Niche = niche;
        WhatYouSell = whatYouSell;
        Message = message;
        CreatedAt = createdAt;
    }

    public static Inquiry Create(string name, EmailAddress email, string niche,
        string whatYouSell, string? message, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Inquiry name is required.");
        if (email is null) throw new DomainException("Inquiry email is required.");
        return new Inquiry(InquiryId.New(), name.Trim(), email, niche?.Trim() ?? "",
            whatYouSell?.Trim() ?? "", string.IsNullOrWhiteSpace(message) ? null : message.Trim(), createdAt);
    }
}
```

- [ ] **Step 4: Run domain test to green**

Run: `dotnet test tests/Coldstart.Domain.Tests --filter InquiryTests`
Expected: PASS.

- [ ] **Step 5: Write the failing SubmitInquiry test**

`tests/Coldstart.Application.Tests/SubmitInquiryTests.cs`:

```csharp
using Coldstart.Application.Contact;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class SubmitInquiryTests
{
    private static SubmitInquiryRequest Req() => new("Sam", "sam@acme.com", "B2B SaaS", "scheduling", "hi");

    [Fact]
    public async Task SubmitAsync_StoresInquiry_AndSendsNotification()
    {
        // Arrange
        var repo = Substitute.For<IInquiryRepository>();
        var email = Substitute.For<IEmailSender>();
        var sut = new SubmitInquiry(repo, email);

        // Act
        await sut.SubmitAsync(Req(), CancellationToken.None);

        // Assert
        await repo.Received(1).AddAsync(Arg.Is<Inquiry>(i => i.Name == "Sam" && i.Email.Value == "sam@acme.com"), Arg.Any<CancellationToken>());
        await email.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Is<string>(b => b.Contains("Sam")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenEmailSenderThrows_StillSucceeds()
    {
        // Arrange
        var repo = Substitute.For<IInquiryRepository>();
        var email = Substitute.For<IEmailSender>();
        email.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("smtp down"));
        var sut = new SubmitInquiry(repo, email);

        // Act
        var act = async () => await sut.SubmitAsync(Req(), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        await repo.Received(1).AddAsync(Arg.Any<Inquiry>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 6: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Application.Tests --filter SubmitInquiryTests`
Expected: FAIL — types missing.

- [ ] **Step 7: Implement ports, request, use case, DI**

`src/Coldstart.Application/Ports/IInquiryRepository.cs`:

```csharp
using Coldstart.Domain;

namespace Coldstart.Application.Ports;

public interface IInquiryRepository
{
    Task AddAsync(Inquiry inquiry, CancellationToken ct);
}
```

`src/Coldstart.Application/Ports/IEmailSender.cs`:

```csharp
namespace Coldstart.Application.Ports;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}
```

`src/Coldstart.Application/Contact/SubmitInquiryRequest.cs`:

```csharp
namespace Coldstart.Application.Contact;

public sealed record SubmitInquiryRequest(string Name, string Email, string Niche, string WhatYouSell, string? Message);
```

`src/Coldstart.Application/Contact/ISubmitInquiry.cs`:

```csharp
namespace Coldstart.Application.Contact;

public interface ISubmitInquiry
{
    Task SubmitAsync(SubmitInquiryRequest request, CancellationToken ct);
}
```

`src/Coldstart.Application/Contact/SubmitInquiry.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Contact;

internal sealed class SubmitInquiry(IInquiryRepository inquiries, IEmailSender email) : ISubmitInquiry
{
    private const string NotifyTo = "andrzej.lech.andrzej@gmail.com";

    public async Task SubmitAsync(SubmitInquiryRequest request, CancellationToken ct)
    {
        var inquiry = Inquiry.Create(request.Name, new EmailAddress(request.Email),
            request.Niche, request.WhatYouSell, request.Message, DateTimeOffset.UtcNow);
        await inquiries.AddAsync(inquiry, ct);

        var body = $"New Coldstart inquiry\n\nName: {inquiry.Name}\nEmail: {inquiry.Email}\n" +
                   $"Niche: {inquiry.Niche}\nSells: {inquiry.WhatYouSell}\nMessage: {inquiry.Message ?? "(none)"}";
        try
        {
            await email.SendAsync(NotifyTo, $"New inquiry from {inquiry.Name}", body, ct);
        }
        catch
        {
            // Inquiry is already persisted; a failed notification must not fail the request.
        }
    }
}
```

Add to `src/Coldstart.Application/DependencyInjection.cs` (inside `AddApplication`):

```csharp
        services.AddScoped<Contact.ISubmitInquiry, Contact.SubmitInquiry>();
```

- [ ] **Step 8: Run to green, build, commit**

Run: `dotnet test tests/Coldstart.Application.Tests --filter SubmitInquiryTests` → PASS; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Domain src/Coldstart.Application tests/Coldstart.Domain.Tests/InquiryTests.cs tests/Coldstart.Application.Tests/SubmitInquiryTests.cs
git commit -m "feat(app): Inquiry aggregate + SubmitInquiry use case (store + notify)"
```

---

### Task 4: Infrastructure — InquiryRepository, SmtpEmailSender, mapping, migration

**Files:**
- Create: `src/Coldstart.Infrastructure/Configurations/InquiryConfiguration.cs`, `Repositories/InquiryRepository.cs`, `Email/SmtpOptions.cs`, `Email/SmtpEmailSender.cs`
- Modify: `src/Coldstart.Infrastructure/ColdstartDbContext.cs` (add `DbSet<Inquiry>`), `DependencyInjection.cs`
- Test: `tests/Coldstart.Infrastructure.Tests/InquiryRepositoryTests.cs`

**Interfaces:**
- Consumes: `IInquiryRepository`, `IEmailSender` (Task 3).
- Produces: EF-backed `InquiryRepository`; MailKit `SmtpEmailSender` bound to `SmtpOptions`; an `AddInquiry` EF migration for the `inquiries` table (required so the Web app's startup `MigrateAsync` has the table by Task 5).

- [ ] **Step 1: Add MailKit + write the failing repo test**

```bash
dotnet add src/Coldstart.Infrastructure package MailKit
```

`tests/Coldstart.Infrastructure.Tests/InquiryRepositoryTests.cs`:

```csharp
using Coldstart.Domain;
using Coldstart.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class InquiryRepositoryTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task AddAsync_PersistsInquiry_Readable()
    {
        // Arrange
        await using var write = fixture.NewDbContext();
        var repo = new InquiryRepository(write);
        var inquiry = Inquiry.Create("Sam", new EmailAddress("sam@acme.com"), "B2B SaaS", "scheduling", "hi", DateTimeOffset.UtcNow);

        // Act
        await repo.AddAsync(inquiry, CancellationToken.None);

        // Assert
        await using var read = fixture.NewDbContext();
        var found = await read.Set<Inquiry>().SingleAsync(i => i.Id == inquiry.Id);
        found.Name.Should().Be("Sam");
        found.Email.Value.Should().Be("sam@acme.com");
    }
}
```

Note: `PostgresFixture` (SP1) uses `EnsureCreated`, so it builds the schema from the model — the new `DbSet<Inquiry>` + config are picked up without a migration for tests.

- [ ] **Step 2: Run to verify it fails**

Run: `docker compose up -d db && dotnet test tests/Coldstart.Infrastructure.Tests --filter InquiryRepositoryTests`
Expected: FAIL — `InquiryRepository`/mapping missing.

- [ ] **Step 3: Implement mapping, repository, SMTP sender, DI**

Add to `ColdstartDbContext.cs`: `public DbSet<Inquiry> Inquiries => Set<Inquiry>();` (keep `ApplyConfigurationsFromAssembly`).

`src/Coldstart.Infrastructure/Configurations/InquiryConfiguration.cs`:

```csharp
using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coldstart.Infrastructure.Configurations;

internal sealed class InquiryConfiguration : IEntityTypeConfiguration<Inquiry>
{
    public void Configure(EntityTypeBuilder<Inquiry> builder)
    {
        builder.ToTable("inquiries");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasConversion(id => id.Value, value => new InquiryId(value))
            .ValueGeneratedNever();
        builder.Property(i => i.Name).IsRequired();
        builder.Property(i => i.Email)
            .HasConversion(e => e.Value, v => new EmailAddress(v))
            .IsRequired();
        builder.Property(i => i.Niche);
        builder.Property(i => i.WhatYouSell);
        builder.Property(i => i.Message);
        builder.Property(i => i.CreatedAt);
    }
}
```

`src/Coldstart.Infrastructure/Repositories/InquiryRepository.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Infrastructure.Repositories;

internal sealed class InquiryRepository(ColdstartDbContext db) : IInquiryRepository
{
    public async Task AddAsync(Inquiry inquiry, CancellationToken ct)
    {
        await db.Inquiries.AddAsync(inquiry, ct);
        await db.SaveChangesAsync(ct);
    }
}
```

`src/Coldstart.Infrastructure/Email/SmtpOptions.cs`:

```csharp
namespace Coldstart.Infrastructure.Email;

public sealed class SmtpOptions
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 465;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromName { get; set; } = "Coldstart";
    public string FromAddress { get; set; } = "";
}
```

`src/Coldstart.Infrastructure/Email/SmtpEmailSender.cs`:

```csharp
using Coldstart.Application.Ports;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Coldstart.Infrastructure.Email;

internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    private readonly SmtpOptions _o = options.Value;

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_o.FromName, string.IsNullOrWhiteSpace(_o.FromAddress) ? _o.Username : _o.FromAddress));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(_o.Host, _o.Port, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(_o.Username, _o.Password, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }
}
```

Add to `src/Coldstart.Infrastructure/DependencyInjection.cs` (inside `AddInfrastructure`, before `return`):

```csharp
        services.AddScoped<IInquiryRepository, Repositories.InquiryRepository>();
        services.Configure<Email.SmtpOptions>(o => { }); // bound from config in Web (Task 6)
        services.AddScoped<IEmailSender, Email.SmtpEmailSender>();
```

(Bring `using Coldstart.Application.Ports;` into the file.)

Also re-add the use-case registration in `src/Coldstart.Application/DependencyInjection.cs` inside `AddApplication` (it was deferred from Task 3 so the Web host's DI validation stayed valid until these adapters existed):

```csharp
        services.AddScoped<Contact.ISubmitInquiry, Contact.SubmitInquiry>();
```

- [ ] **Step 4: Create the AddInquiry migration**

The Web app applies migrations on startup (`MigrateAsync`), so the `inquiries` table must exist as a migration (not just via `EnsureCreated`, which only the tests use) before Task 5's contact POST runs against the web host:

```bash
dotnet ef migrations add AddInquiry -p src/Coldstart.Infrastructure -s src/Coldstart.Infrastructure -o Migrations
```

Verify it adds the `inquiries` table (and touches nothing else).

- [ ] **Step 5: Run repo test to green, build, commit**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter InquiryRepositoryTests` → PASS; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Infrastructure tests/Coldstart.Infrastructure.Tests/InquiryRepositoryTests.cs
git commit -m "feat(infra): Inquiry EF mapping + repository + MailKit SMTP sender + migration"
```

---

### Task 5: Contact modal server handler

**Files:**
- Create: `src/Coldstart.Web/Pages/Contact.cshtml.cs` (handler-only page model), update `wwwroot/js/contact.js` to POST
- Modify: `src/Coldstart.Web/Pages/Index.cshtml` (modal form posts to `/Contact`, add honeypot + antiforgery)
- Test: `tests/Coldstart.Web.Tests/ContactTests.cs`, and a fake sender helper in the Web.Tests project

**Interfaces:**
- Consumes: `ISubmitInquiry` (Task 3). Produces: `POST /Contact` endpoint that validates + submits.

- [ ] **Step 1: Add a fake email sender to the Web test host**

Create `tests/Coldstart.Web.Tests/FakeEmailSender.cs`:

```csharp
using Coldstart.Application.Ports;

namespace Coldstart.Web.Tests;

public sealed class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Body)> Sent { get; } = new();

    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        Sent.Add((to, subject, body));
        return Task.CompletedTask;
    }
}
```

In `ColdstartWebFactory.ConfigureWebHost`, replace the real `IEmailSender` with a shared `FakeEmailSender` singleton and expose it:

```csharp
    public FakeEmailSender Email { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Coldstart", _db.GetConnectionString());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
        });
    }
```

(Add `using Coldstart.Application.Ports;`, `using Microsoft.Extensions.DependencyInjection;`, `using Microsoft.Extensions.DependencyInjection.Extensions;`.)

- [ ] **Step 2: Write the failing contact tests**

`tests/Coldstart.Web.Tests/ContactTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Coldstart.Infrastructure;
using Coldstart.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coldstart.Web.Tests;

public class ContactTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task PostContact_Valid_StoresInquiry_AndNotifies()
    {
        // Arrange
        var client = factory.CreateClient();
        var form = await AntiforgeryForm(client, new()
        {
            ["Name"] = "Sam", ["Email"] = "sam@acme.com",
            ["Niche"] = "B2B SaaS", ["WhatYouSell"] = "scheduling", ["Message"] = "hi", ["website"] = ""
        });

        // Act
        var response = await client.PostAsync("/Contact", new FormUrlEncodedContent(form));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        (await db.Set<Inquiry>().CountAsync(i => i.Name == "Sam")).Should().Be(1);
        factory.Email.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task PostContact_HoneypotFilled_DoesNotStoreOrNotify()
    {
        // Arrange
        var client = factory.CreateClient();
        var form = await AntiforgeryForm(client, new()
        {
            ["Name"] = "Bot", ["Email"] = "bot@spam.com",
            ["Niche"] = "x", ["WhatYouSell"] = "y", ["website"] = "http://spam"
        });
        var before = factory.Email.Sent.Count;

        // Act
        var response = await client.PostAsync("/Contact", new FormUrlEncodedContent(form));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Email.Sent.Count.Should().Be(before);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        (await db.Set<Inquiry>().CountAsync(i => i.Name == "Bot")).Should().Be(0);
    }

    // Fetches the landing page, extracts the antiforgery token, and merges it into the form.
    private static async Task<Dictionary<string, string>> AntiforgeryForm(HttpClient client, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync("/");
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

(If the antiforgery token markup differs, the implementer adjusts the marker or disables antiforgery validation for the handler; keep antiforgery on in production.)

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests --filter ContactTests`
Expected: FAIL — `/Contact` 404.

- [ ] **Step 4: Implement the handler + wire the form**

`src/Coldstart.Web/Pages/Contact.cshtml.cs`:

```csharp
using Coldstart.Application.Contact;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Pages;

public sealed class ContactModel(ISubmitInquiry submit) : PageModel
{
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Niche { get; set; } = "";
    [BindProperty] public string WhatYouSell { get; set; } = "";
    [BindProperty] public string? Message { get; set; }
    [BindProperty] public string? Website { get; set; } // honeypot — must stay empty

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        // Honeypot: bots fill hidden fields. Silently succeed without storing/sending.
        if (!string.IsNullOrWhiteSpace(Website))
            return new OkResult();

        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Email) || !Email.Contains('@'))
            return BadRequest("Name and a valid email are required.");

        await submit.SubmitAsync(new SubmitInquiryRequest(Name, Email, Niche, WhatYouSell, Message), ct);
        return new OkResult();
    }
}
```

Create `src/Coldstart.Web/Pages/Contact.cshtml` (needed so the page/handler routes):

```html
@page
@model Coldstart.Web.Pages.ContactModel
```

In `Pages/Index.cshtml`, make the modal a real form posting to `/Contact` (keep the visual structure from the port): wrap the modal inputs in `<form method="post" action="/Contact">`, add `@Html.AntiForgeryToken()` equivalent (in Razor Pages the tag helper auto-adds it for `<form method="post">`), add the visitor **email** input (`name="Email"`), map existing inputs to `name="Name"`, `name="Niche"`, `name="WhatYouSell"`, `name="Message"`, and add a visually-hidden honeypot `<input name="Website" tabindex="-1" autocomplete="off" class="hp">` with CSS `.hp{position:absolute;left:-9999px}` in site.css. Keep the `mailto:` link as a fallback `<a>`. Update `wwwroot/js/contact.js` to submit the form via `fetch` and show a success state (progressive enhancement; the plain form post also works without JS).

- [ ] **Step 5: Run to green, build, commit**

Run: `dotnet test tests/Coldstart.Web.Tests --filter ContactTests` → PASS; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Web tests/Coldstart.Web.Tests
git commit -m "feat(web): server-side contact form (store + notify) with honeypot"
```

---

### Task 6: Identity foundation + Google wiring + migration

**Files:**
- Create: `src/Coldstart.Infrastructure/Identity/ApplicationUser.cs`
- Modify: `src/Coldstart.Infrastructure/ColdstartDbContext.cs` (→ `IdentityDbContext<ApplicationUser>`), `DesignTimeDbContextFactory.cs` (unchanged type), `src/Coldstart.Web/Program.cs`, `src/Coldstart.Web/appsettings.json`
- Create migration: `src/Coldstart.Infrastructure/Migrations/*` (Identity + Inquiry + CustomerId)
- Test: `tests/Coldstart.Web.Tests/AuthFlowTests.cs` (redirect check only in this task)

**Interfaces:**
- Consumes: everything so far. Produces: `ApplicationUser : IdentityUser { Guid? CustomerId }`; Identity configured (email+password + Google); `[Authorize]` protects `/app`.

- [ ] **Step 1: Add Identity + Google packages**

```bash
dotnet add src/Coldstart.Infrastructure package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet add src/Coldstart.Web package Microsoft.AspNetCore.Authentication.Google
```

- [ ] **Step 2: ApplicationUser + DbContext becomes IdentityDbContext**

`src/Coldstart.Infrastructure/Identity/ApplicationUser.cs`:

```csharp
using Microsoft.AspNetCore.Identity;

namespace Coldstart.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public Guid? CustomerId { get; set; }
}
```

Change `ColdstartDbContext.cs` to:

```csharp
using Coldstart.Domain;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Infrastructure;

public sealed class ColdstartDbContext(DbContextOptions<ColdstartDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Inquiry> Inquiries => Set<Inquiry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // Identity schema
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ColdstartDbContext).Assembly);
    }
}
```

- [ ] **Step 3: Add the Identity migration (incremental)**

Add a new migration on top of SP1's `InitialCreate` and Task 4's `AddInquiry` — it should generate the Identity tables (`AspNetUsers`, roles, logins, tokens, etc.) plus the `CustomerId` column on `AspNetUsers`:

```bash
dotnet ef migrations add AddIdentity -p src/Coldstart.Infrastructure -s src/Coldstart.Infrastructure -o Migrations
```

Verify it adds only Identity tables + the `CustomerId` column (does not drop `customers`/`leads`/`inquiries`).

- [ ] **Step 4: Wire Identity + Google in Program.cs**

Update `src/Coldstart.Web/Program.cs` — after `AddInfrastructure(...)`:

```csharp
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
// ...
builder.Services.Configure<Coldstart.Infrastructure.Email.SmtpOptions>(builder.Configuration.GetSection("Smtp"));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(o => { o.SignIn.RequireConfirmedAccount = false; })
    .AddEntityFrameworkStores<ColdstartDbContext>()
    .AddDefaultTokenProviders();

var authBuilder = builder.Services.AddAuthentication();
var googleId = builder.Configuration["Authentication:Google:ClientId"];
var googleSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleId) && !string.IsNullOrWhiteSpace(googleSecret))
{
    authBuilder.AddGoogle(o => { o.ClientId = googleId; o.ClientSecret = googleSecret; });
}
```

And in the pipeline (after `UseRouting()`): `app.UseAuthentication(); app.UseAuthorization();`. Add `builder.Services.AddRazorPages(o => o.Conventions.AuthorizeAreaFolder("App", "/"));` (or `[Authorize]` on the App page model — Task 8). Add a `"Smtp"` section to `appsettings.json` with empty `Username`/`Password` (real values via user-secrets).

- [ ] **Step 5: Write the failing redirect test**

`tests/Coldstart.Web.Tests/AuthFlowTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class AuthFlowTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task GetApp_WhenNotAuthenticated_RedirectsToLogin()
    {
        // Arrange
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/app");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");
    }
}
```

- [ ] **Step 6: Run (fails until /app + Identity UI exist), build, commit**

The redirect test needs `/app` to exist + Identity login route. If `/app` isn't created until Task 8, this test will 404 rather than redirect — so gate it: implement the minimal authorized `/app` stub here (empty `[Authorize]` page) OR move this test to Task 8. **Decision:** create a minimal `Areas/App/Pages/Index.cshtml(.cs)` stub with `[Authorize]` now (fleshed out in Task 8) so the redirect works. Scaffold Identity UI:

```bash
dotnet add src/Coldstart.Web package Microsoft.VisualStudio.Web.CodeGeneration.Design
dotnet tool install -g dotnet-aspnet-codegenerator 2>/dev/null || dotnet tool update -g dotnet-aspnet-codegenerator
dotnet aspnet-codegenerator identity -dc Coldstart.Infrastructure.ColdstartDbContext --userClass ApplicationUser --files "Account.Login;Account.Register;Account.Logout;Account.ExternalLogin" -p src/Coldstart.Web
```

Then: build, run `dotnet test tests/Coldstart.Web.Tests --filter AuthFlowTests` → PASS (redirect to login).
```bash
git add src/Coldstart.Infrastructure src/Coldstart.Web tests/Coldstart.Web.Tests/AuthFlowTests.cs
git commit -m "feat(auth): ASP.NET Identity (email+password + optional Google) + Identity migration"
```

**If `aspnet-codegenerator` is unavailable/awkward in this environment, STOP and report** — the controller may prefer hand-written minimal Register/Login/Logout Razor pages (Task 7 covers their behavior); note it and proceed with hand-written pages.

---

### Task 7: Registration provisioning — create Customer + seed mock leads

**Files:**
- Create: `src/Coldstart.Application/Customers/IProvisionCustomer.cs`, `Customers/ProvisionCustomer.cs`; update `DependencyInjection.cs`
- Modify: the scaffolded/handwritten `Register` page (and `ExternalLogin` confirmation) to call provisioning + set `user.CustomerId`
- Test: `tests/Coldstart.Application.Tests/ProvisionCustomerTests.cs`, `tests/Coldstart.Web.Tests/AuthFlowTests.cs` (register → seeded)

**Interfaces:**
- Consumes: `ICustomerRepository` (SP1), `Customer` domain.
- Produces: `interface IProvisionCustomer { Task<Guid> ProvisionAsync(string customerName, CancellationToken ct); }` — creates a `Customer`, seeds mock leads, returns the new customer's Guid.

- [ ] **Step 1: Write the failing ProvisionCustomer test**

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
    public async Task ProvisionAsync_CreatesCustomerWithSeededLeads_AndReturnsId()
    {
        // Arrange
        var customers = Substitute.For<ICustomerRepository>();
        Customer? saved = null;
        await customers.AddAsync(Arg.Do<Customer>(c => saved = c), Arg.Any<CancellationToken>());
        var sut = new ProvisionCustomer(customers);

        // Act
        var id = await sut.ProvisionAsync("sam@acme.com", CancellationToken.None);

        // Assert
        saved.Should().NotBeNull();
        saved!.Leads.Should().NotBeEmpty();       // mock leads seeded
        id.Should().Be(saved.Id.Value);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Application.Tests --filter ProvisionCustomerTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement ProvisionCustomer + register in DI**

`src/Coldstart.Application/Customers/IProvisionCustomer.cs`:

```csharp
namespace Coldstart.Application.Customers;

public interface IProvisionCustomer
{
    Task<Guid> ProvisionAsync(string customerName, CancellationToken ct);
}
```

`src/Coldstart.Application/Customers/ProvisionCustomer.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

internal sealed class ProvisionCustomer(ICustomerRepository customers) : IProvisionCustomer
{
    public async Task<Guid> ProvisionAsync(string customerName, CancellationToken ct)
    {
        var customer = Customer.Create(customerName);
        var now = DateTimeOffset.UtcNow;
        // Mock leads until the harvest pipeline lands.
        customer.AddLead("Patronus AI", Fit.Yes, "Anand — a thought after the $50M", "…", now);
        customer.AddLead("Lago", Fit.Yes, "hiring GTM at Lago", "…", now);
        customer.AddLead("Visiblie", Fit.Maybe, "congrats on the €500K", "…", now);
        await customers.AddAsync(customer, ct);
        return customer.Id.Value;
    }
}
```

Add to `AddApplication`: `services.AddScoped<Customers.IProvisionCustomer, Customers.ProvisionCustomer>();`

- [ ] **Step 4: Run to green**

Run: `dotnet test tests/Coldstart.Application.Tests --filter ProvisionCustomerTests`
Expected: PASS.

- [ ] **Step 5: Hook provisioning into registration**

In the `Register` page's `OnPostAsync`, after `_userManager.CreateAsync(user, Input.Password)` succeeds and before sign-in, provision + stamp the CustomerId:

```csharp
// inject IProvisionCustomer provision into the page model ctor
if (user.CustomerId is null)
{
    user.CustomerId = await provision.ProvisionAsync(user.Email!, CancellationToken.None);
    await _userManager.UpdateAsync(user);
}
```

Do the same in `ExternalLogin`'s confirmation `OnPostConfirmationAsync` right after the external user is created (so Google sign-ups also get a provisioned customer).

- [ ] **Step 6: Add the register→seeded integration test**

Add to `AuthFlowTests.cs` a test that posts to `/Identity/Account/Register` with a unique email + password, follows to an authenticated state, and asserts the created `ApplicationUser` has a non-null `CustomerId` and that customer has leads (query via a scope + `UserManager`/`ColdstartDbContext`). (The implementer wires the antiforgery token as in ContactTests.)

```csharp
    [Fact]
    public async Task Register_ProvisionsCustomerWithSeededLeads()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@test.com";

        // Act
        await RegisterAsync(client, email, "Passw0rd!");   // helper: GET register, post with antiforgery token

        // Assert
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ColdstartDbContext>();
        var user = await users.FindByEmailAsync(email);
        user!.CustomerId.Should().NotBeNull();
        (await db.Leads.CountAsync(l => l.CustomerId == new CustomerId(user.CustomerId!.Value))).Should().BeGreaterThan(0);
    }
```

- [ ] **Step 7: Run to green, build, commit**

Run: `dotnet test tests/Coldstart.Web.Tests --filter AuthFlowTests` + `dotnet test tests/Coldstart.Application.Tests --filter ProvisionCustomerTests` → PASS; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Application src/Coldstart.Web tests
git commit -m "feat(auth): provision a seeded Customer per new user on registration"
```

---

### Task 8: Customer zone — list the user's leads

**Files:**
- Modify: `src/Coldstart.Application/Leads/IGetLeadInbox.cs` + `Leads/GetLeadInbox.cs` (add `ForCustomerAsync(Guid)`, remove `ForFirstCustomerAsync`), `tests/Coldstart.Application.Tests/GetLeadInboxTests.cs`
- Create/flesh out: `src/Coldstart.Web/Areas/App/Pages/Index.cshtml(.cs)`
- Test: `tests/Coldstart.Web.Tests/CustomerZoneTests.cs`

**Interfaces:**
- Consumes: `IGetLeadInbox`, `UserManager<ApplicationUser>`.
- Produces: `IGetLeadInbox.ForCustomerAsync(Guid customerId, CancellationToken ct) → Task<LeadInbox>`; the authorized `/app` page.

- [ ] **Step 1: Update the GetLeadInbox use case + its test**

Replace `ForFirstCustomerAsync` with:

`IGetLeadInbox.cs`:

```csharp
namespace Coldstart.Application.Leads;

public interface IGetLeadInbox
{
    Task<LeadInbox> ForCustomerAsync(Guid customerId, CancellationToken ct);
}
```

`GetLeadInbox.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Leads;

internal sealed class GetLeadInbox(ICustomerRepository customers, IListLeads listLeads) : IGetLeadInbox
{
    public async Task<LeadInbox> ForCustomerAsync(Guid customerId, CancellationToken ct)
    {
        var id = new CustomerId(customerId);
        var all = await customers.AllAsync(ct);
        var customer = all.FirstOrDefault(c => c.Id == id);
        if (customer is null)
            return new LeadInbox("—", Array.Empty<LeadSummary>());

        var leads = await listLeads.ForCustomerAsync(id, ct);
        return new LeadInbox(customer.Name, leads);
    }
}
```

Update `GetLeadInboxTests.cs` to call `ForCustomerAsync(customer.Id.Value, ...)` (mock `ICustomerRepository.AllAsync` to return the customer and `IListLeads.ForCustomerAsync` to return summaries); keep the happy + empty cases (empty = unknown customer id → `"—"` + empty).

- [ ] **Step 2: Run the updated use-case test**

Run: `dotnet test tests/Coldstart.Application.Tests --filter GetLeadInbox`
Expected: PASS after the update.

- [ ] **Step 3: Write the failing customer-zone tests**

`tests/Coldstart.Web.Tests/CustomerZoneTests.cs`: register user A (seeded), log in, GET `/app`, assert 200 + shows a seeded company (e.g. "Patronus AI"). Second test: register user B, ensure B's `/app` shows B's leads and the page is scoped to the logged-in user (tenant isolation — assert the leads belong to B's customer; since both are seeded with the same mock set, assert the customer name / that A cannot see a lead id belonging only to B by checking counts are per-user, or seed A and B with distinguishable customer names derived from their email and assert the page shows the logged-in user's customer name). Use the antiforgery+login helpers from AuthFlowTests.

```csharp
    [Fact]
    public async Task App_ShowsLoggedInUsersOwnCustomerLeads()
    {
        // Arrange
        var client = factory.CreateClient();
        var email = $"zone-{Guid.NewGuid():N}@test.com";
        await RegisterAsync(client, email, "Passw0rd!");   // also signs in

        // Act
        var html = await client.GetStringAsync("/app");

        // Assert
        html.Should().Contain("Patronus AI");   // seeded mock lead
        html.Should().Contain(email);           // customer named from the user's email
    }
```

- [ ] **Step 4: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests --filter CustomerZoneTests`
Expected: FAIL — `/app` stub returns no leads yet.

- [ ] **Step 5: Implement the zone page**

`src/Coldstart.Web/Areas/App/Pages/Index.cshtml.cs`:

```csharp
using Coldstart.Application.Leads;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.App.Pages;

[Authorize]
public sealed class IndexModel(UserManager<ApplicationUser> users, IGetLeadInbox inbox) : PageModel
{
    public string CustomerName { get; private set; } = "—";
    public IReadOnlyList<LeadSummary> Leads { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user?.CustomerId is null) return;
        var result = await inbox.ForCustomerAsync(user.CustomerId.Value, ct);
        CustomerName = result.CustomerName;
        Leads = result.Leads;
    }
}
```

`src/Coldstart.Web/Areas/App/Pages/Index.cshtml`: `@page "/app"`, `Layout="/Pages/Shared/_Layout.cshtml"`, render a heading with `@Model.CustomerName` + a table of `@Model.Leads` (Company/Fit/Status/Recipient/Confidence), matching the SP1 table markup. Ensure the App area is registered (`Areas/App/Pages/_ViewStart.cshtml` if needed).

- [ ] **Step 6: Run to green, build, commit**

Run: `dotnet test tests/Coldstart.Web.Tests --filter CustomerZoneTests` + full `dotnet test Coldstart.sln` → PASS; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Application src/Coldstart.Web tests
git commit -m "feat(app): authorized customer zone lists the user's seeded leads"
```

---

### Task 9: Google external login + merge-by-email

**Files:**
- Modify: the scaffolded `ExternalLogin` page (merge-by-email + provisioning), `Login`/`Register` pages (show Google button when configured), `wwwroot/css/site.css` (button style)
- Test: `tests/Coldstart.Web.Tests/AuthFlowTests.cs` (Google button visibility), plus a unit test of the merge decision if the logic is extracted

**Interfaces:**
- Consumes: Identity external-login APIs, `IProvisionCustomer` (Task 7).
- Produces: working Google sign-in that links to an existing local account by email (merge) or creates+provisions a new one.

- [ ] **Step 1: Merge-by-email in ExternalLogin confirmation**

In the scaffolded `ExternalLogin.OnGetCallbackAsync`, before creating a new user, look up by the email claim and link the login if found:

```csharp
var emailClaim = info.Principal.FindFirstValue(ClaimTypes.Email);
if (!string.IsNullOrEmpty(emailClaim))
{
    var existing = await _userManager.FindByEmailAsync(emailClaim);
    if (existing is not null)
    {
        await _userManager.AddLoginAsync(existing, info);        // merge
        await _signInManager.SignInAsync(existing, isPersistent: false);
        return LocalRedirect(returnUrl);
    }
}
// else fall through to the create-new-user path, then provision (Task 7 hook)
```

- [ ] **Step 2: Show the Google button only when configured**

In `Login.cshtml`/`Register.cshtml`, the scaffolded external-providers loop already renders a button per registered scheme. Since Google is only added when creds exist (Task 6), the button appears automatically when configured and is absent otherwise. Style the provider button in `site.css` to match the brand.

- [ ] **Step 3: Test button visibility**

Add to `AuthFlowTests.cs`: with no Google creds configured (the test host sets none), GET `/Identity/Account/Login` returns 200 and does **not** contain a Google button; email+password form is present. (End-to-end Google sign-in is verified manually once real creds are supplied — note this in the report.)

```csharp
    [Fact]
    public async Task Login_WithoutGoogleConfigured_ShowsEmailPasswordOnly()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var html = await client.GetStringAsync("/Identity/Account/Login");

        // Assert
        html.Should().Contain("Password");
        html.Should().NotContain("Google");
    }
```

- [ ] **Step 4: Run to green, build, commit**

Run: `dotnet test Coldstart.sln` → all PASS; `dotnet build Coldstart.sln` → 0 warnings.
```bash
git add src/Coldstart.Web tests/Coldstart.Web.Tests/AuthFlowTests.cs
git commit -m "feat(auth): Google external login with merge-by-email"
```

- [ ] **Step 5: Document local Google + SMTP secrets**

Append to the repo's dev notes (or a new `src/Coldstart.Web/README.md`): how to set user-secrets for `Authentication:Google:ClientId/ClientSecret` and `Smtp:Username/Password`, and the Google OAuth redirect URI (`/signin-google`). Commit.

---

## Self-Review

**Spec coverage:**
- Landing ported 1:1 → Task 1. ✓
- Privacy page (starter draft, Schiedam/KvK/VAT) → Task 2. ✓
- Server-side contact form (store `Inquiry` + notify, honeypot, mailto fallback, visitor email field) → Tasks 3–5. ✓
- MailKit SMTP sender → Task 4. ✓
- Identity email+password + Google + merge-by-email → Tasks 6, 9. ✓
- `ApplicationUser.CustomerId`, Identity in `ColdstartDbContext`, migration → Task 6. ✓
- Per-user Customer provisioning + seeded mock leads → Task 7. ✓
- Authorized `/app` zone showing the user's leads + tenant isolation test → Task 8. ✓
- Testing (WebApplicationFactory + Testcontainers + fake sender; unit tests) → throughout. ✓
- Non-goals (no harvest/AI, billing, password reset) → respected. ✓

**Placeholder scan:** The brand port (Task 1) references `site/index.html` as the concrete source rather than pasting 700 lines — intentional (the content exists in-repo). Seed draft bodies use "…" as seed content. The privacy KvK/VAT are genuine unknowns marked for the user. Identity UI uses `aspnet-codegenerator` (concrete command) with a hand-written fallback path called out. No vague "add error handling" steps.

**Type consistency:** `ISubmitInquiry.SubmitAsync(SubmitInquiryRequest, ct)`, `IEmailSender.SendAsync(to, subject, body, ct)`, `IInquiryRepository.AddAsync(Inquiry, ct)`, `IProvisionCustomer.ProvisionAsync(string, ct)→Guid`, `IGetLeadInbox.ForCustomerAsync(Guid, ct)→LeadInbox`, `ApplicationUser.CustomerId (Guid?)`, `SmtpOptions` — used consistently across Tasks 3–9. `ForFirstCustomerAsync` is explicitly removed and replaced in Task 8 (with the SP1 `/Leads` page + its test removed in Task 1), so no dangling references remain.

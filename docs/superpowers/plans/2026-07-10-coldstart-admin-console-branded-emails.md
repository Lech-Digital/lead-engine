# Admin Console + Branded Transactional Emails Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn `/admin` into a SuperAdmin operations console (invite management + companies/users overview with block & remove) and give the invitation and password-reset emails a branded, deliverability-safe HTML treatment.

**Architecture:** The `IEmailSender` port gains an optional HTML alternative; `SmtpEmailSender` sends `multipart/alternative`. A Web-layer `EmailTemplates` builder renders a shared branded HTML shell (no images). The Admin Razor page gains named POST handlers and a unified user table. Tenant deletion is a new Application port (`IDeleteCustomer`) with an Infrastructure adapter. No database migration.

**Tech Stack:** .NET 10, ASP.NET Core Razor Pages, ASP.NET Core Identity + EF Core (Postgres), MailKit/MimeKit SMTP, xUnit + FluentAssertions, Testcontainers Postgres + `FakeEmailSender`.

## Global Constraints

- Hexagonal/DDD (CS-HEX): Domain zero external deps; ports in Application; DI in composition roots. HTML building stays in Web; `IEmailSender` stays transport-only. No `DbContext`/`IQueryable` in Web business logic (CS-DATA-01) — tenant deletion goes through `IDeleteCustomer`.
- Async all the way; `CancellationToken` through IO (CS-DATA-03). `internal` for implementations (CS-MOD-01); `record` for DTOs/VOs (CS-DDD-01).
- Build stays at **0 warnings**; full suite green after every task. **No `Co-Authored-By` trailers.**
- Roles: `SuperAdmin`/`CompanyAdmin`/`User`; SuperAdmin email = `ColdstartRoles.SuperAdminEmail`. Reset-token lifespan is 7 days.
- Sender identity `noreply@getcoldstart.nl`; brand palette: cream `#F3E9D6`/`#FBF4E6`, ink `#382C23`/`#675541`, orange `#E07A3E`/dark `#A04E1E`, rainbow `#E07A3E`,`#D9897F`,`#E0A93B`,`#5E9E94`,`#6E6AB0`.
- Web tests use `ColdstartWebFactory` (Testcontainers) + `TestAuth`; `factory.Email` is the `FakeEmailSender`. Infrastructure tests use `PostgresFixture` (`fixture.NewDbContext()`).

---

### Task 1: `IEmailSender` carries an optional HTML body; `SmtpEmailSender` sends multipart

**Files:**
- Modify: `src/Coldstart.Application/Ports/IEmailSender.cs`
- Modify: `src/Coldstart.Infrastructure/Email/SmtpEmailSender.cs`
- Modify: `src/Coldstart.Application/Contact/SubmitInquiry.cs`
- Modify: `tests/Coldstart.Web.Tests/FakeEmailSender.cs`
- Modify: `tests/Coldstart.Web.Tests/AdminInviteTests.cs` (`.Body`→`.TextBody`), `tests/Coldstart.Web.Tests/PasswordResetTests.cs` (`.Body`→`.TextBody`)
- Test: `tests/Coldstart.Infrastructure.Tests/SmtpEmailSenderTests.cs` (create)

**Interfaces:**
- Produces:
  - `IEmailSender.SendAsync(string to, string subject, string textBody, string? htmlBody, CancellationToken ct)`
  - `internal static MimeMessage SmtpEmailSender.BuildMessage(SmtpOptions o, string to, string subject, string textBody, string? htmlBody)`
  - `FakeEmailSender.Sent` → `List<(string To, string Subject, string TextBody, string? HtmlBody)>`

- [ ] **Step 1: Write the failing test**

Create `tests/Coldstart.Infrastructure.Tests/SmtpEmailSenderTests.cs`:

```csharp
using Coldstart.Infrastructure.Email;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class SmtpEmailSenderTests
{
    private static readonly SmtpOptions Opts = new()
    {
        Host = "smtp.example.com", Port = 465, Username = "u", Password = "p",
        FromName = "Coldstart", FromAddress = "noreply@getcoldstart.nl",
    };

    [Fact]
    public void BuildMessage_WithHtml_ProducesMultipartWithBothParts()
    {
        // Act
        var msg = SmtpEmailSender.BuildMessage(Opts, "to@x.com", "Subj", "plain text link", "<p>html link</p>");

        // Assert
        msg.From.ToString().Should().Contain("noreply@getcoldstart.nl");
        msg.Subject.Should().Be("Subj");
        msg.TextBody.Should().Contain("plain text link");
        msg.HtmlBody.Should().Contain("html link");
    }

    [Fact]
    public void BuildMessage_WithoutHtml_ProducesPlainTextOnly()
    {
        // Act
        var msg = SmtpEmailSender.BuildMessage(Opts, "to@x.com", "Subj", "plain only", null);

        // Assert
        msg.TextBody.Should().Be("plain only");
        msg.HtmlBody.Should().BeNull();
    }

    [Fact]
    public void BuildMessage_WithBlankFromAddress_FallsBackToUsername()
    {
        // Arrange
        var opts = new SmtpOptions { Username = "user@x.com", FromAddress = "", FromName = "C" };

        // Act
        var msg = SmtpEmailSender.BuildMessage(opts, "to@x.com", "S", "t", null);

        // Assert
        msg.From.ToString().Should().Contain("user@x.com");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests/Coldstart.Infrastructure.Tests.csproj --filter SmtpEmailSenderTests`
Expected: FAIL — `BuildMessage` and the new `SendAsync` overload don't exist (compile error).

- [ ] **Step 3: Change the port**

Replace `src/Coldstart.Application/Ports/IEmailSender.cs` body:

```csharp
namespace Coldstart.Application.Ports;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string textBody, string? htmlBody, CancellationToken ct);
}
```

- [ ] **Step 4: Refactor `SmtpEmailSender` (extract testable `BuildMessage`, send multipart)**

Replace `src/Coldstart.Infrastructure/Email/SmtpEmailSender.cs`:

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

    public async Task SendAsync(string to, string subject, string textBody, string? htmlBody, CancellationToken ct)
    {
        var msg = BuildMessage(_o, to, subject, textBody, htmlBody);

        using var client = new SmtpClient();
        await client.ConnectAsync(_o.Host, _o.Port, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(_o.Username, _o.Password, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }

    internal static MimeMessage BuildMessage(SmtpOptions o, string to, string subject, string textBody, string? htmlBody)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(o.FromName, string.IsNullOrWhiteSpace(o.FromAddress) ? o.Username : o.FromAddress));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = string.IsNullOrEmpty(htmlBody)
            ? new TextPart("plain") { Text = textBody }
            : new BodyBuilder { TextBody = textBody, HtmlBody = htmlBody }.ToMessageBody();
        return msg;
    }
}
```

- [ ] **Step 5: Update the `SubmitInquiry` caller (no HTML)**

In `src/Coldstart.Application/Contact/SubmitInquiry.cs`, change the send call to pass `null` for html:

```csharp
            await email.SendAsync(NotifyTo, $"New inquiry from {inquiry.Name}", body, null, ct);
```

- [ ] **Step 6: Update `FakeEmailSender` to the new signature + capture HTML**

Replace `tests/Coldstart.Web.Tests/FakeEmailSender.cs`:

```csharp
using Coldstart.Application.Ports;

namespace Coldstart.Web.Tests;

public sealed class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject, string TextBody, string? HtmlBody)> Sent { get; } = new();

    public Task SendAsync(string to, string subject, string textBody, string? htmlBody, CancellationToken ct)
    {
        Sent.Add((to, subject, textBody, htmlBody));
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 7: Fix the two existing tests that read `.Body`**

In `tests/Coldstart.Web.Tests/AdminInviteTests.cs`, change the single `.Body` usage to `.TextBody`:

```csharp
        factory.Email.Sent.Single().TextBody.Should().Contain("/Identity/Account/ResetPassword");
```

In `tests/Coldstart.Web.Tests/PasswordResetTests.cs`, change all three `.Body` usages to `.TextBody` (line ~29 assertion, and the two `Regex.Match(factory.Email.Sent.Single().TextBody, ...)`).

- [ ] **Step 8: Run to verify pass (new test + full suite compiles/green)**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests/Coldstart.Infrastructure.Tests.csproj --filter SmtpEmailSenderTests`
Expected: PASS.
Run: `dotnet build Coldstart.sln -clp:ErrorsOnly` then `dotnet test Coldstart.sln`
Expected: 0 warnings; all tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/Coldstart.Application/Ports/IEmailSender.cs src/Coldstart.Infrastructure/Email/SmtpEmailSender.cs src/Coldstart.Application/Contact/SubmitInquiry.cs tests/Coldstart.Web.Tests/FakeEmailSender.cs tests/Coldstart.Web.Tests/AdminInviteTests.cs tests/Coldstart.Web.Tests/PasswordResetTests.cs tests/Coldstart.Infrastructure.Tests/SmtpEmailSenderTests.cs
git commit -m "feat(email): IEmailSender carries optional HTML; SmtpEmailSender sends multipart/alternative"
```

---

### Task 2: Branded email shell + templates (`EmailTemplates`)

**Files:**
- Create: `src/Coldstart.Web/Email/EmailTemplates.cs`
- Modify: `src/Coldstart.Web/Coldstart.Web.csproj` (expose internals to the test project)
- Test: `tests/Coldstart.Web.Tests/EmailTemplatesTests.cs` (create)

**Interfaces:**
- Produces:
  - `internal sealed record Coldstart.Web.Email.EmailContent(string Subject, string TextBody, string HtmlBody)`
  - `internal static EmailContent EmailTemplates.Invitation(string company, string setPasswordUrl)`
  - `internal static EmailContent EmailTemplates.PasswordReset(string setPasswordUrl)`

- [ ] **Step 1: Write the failing test**

Create `tests/Coldstart.Web.Tests/EmailTemplatesTests.cs`:

```csharp
using Coldstart.Web.Email;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class EmailTemplatesTests
{
    private const string Url = "https://getcoldstart.nl/Identity/Account/ResetPassword?code=abc&email=x%40y.com";

    [Fact]
    public void Invitation_IncludesCompanyLinkAndBrandInBothBodies()
    {
        // Act
        var c = EmailTemplates.Invitation("Acme Co", Url);

        // Assert
        c.Subject.Should().NotBeNullOrWhiteSpace();
        c.TextBody.Should().Contain(Url).And.Contain("Acme Co");
        c.HtmlBody.Should().Contain(Url).And.Contain("Acme Co").And.Contain("getcoldstart.nl");
        c.HtmlBody.Should().Contain("Cold").And.Contain("start"); // wordmark
    }

    [Fact]
    public void PasswordReset_IncludesLinkAndBrandInBothBodies()
    {
        // Act
        var c = EmailTemplates.PasswordReset(Url);

        // Assert
        c.TextBody.Should().Contain(Url);
        c.HtmlBody.Should().Contain(Url).And.Contain("getcoldstart.nl");
    }

    [Fact]
    public void Invitation_HtmlEncodesCompanyName()
    {
        // Act — a company name with HTML-significant characters must not inject raw markup
        var c = EmailTemplates.Invitation("A & B <Ltd>", Url);

        // Assert
        c.HtmlBody.Should().Contain("A &amp; B &lt;Ltd&gt;").And.NotContain("<Ltd>");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter EmailTemplatesTests`
Expected: FAIL — `Coldstart.Web.Email.EmailTemplates` not found / not visible (compile error).

- [ ] **Step 3: Expose Web internals to the test project**

In `src/Coldstart.Web/Coldstart.Web.csproj`, add inside the existing `<Project>` (a new `ItemGroup`):

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Coldstart.Web.Tests" />
  </ItemGroup>
```

- [ ] **Step 4: Create `EmailTemplates.cs`**

Create `src/Coldstart.Web/Email/EmailTemplates.cs`:

```csharp
using System.Net;

namespace Coldstart.Web.Email;

internal sealed record EmailContent(string Subject, string TextBody, string HtmlBody);

// Branded, image-free transactional email shell. Table-based + inline CSS so it
// renders across clients and needs no hosted assets while the site isn't deployed.
internal static class EmailTemplates
{
    private const string Site = "https://getcoldstart.nl";

    internal static EmailContent Invitation(string company, string setPasswordUrl) => new(
        Subject: "You've been invited to Coldstart",
        TextBody:
            $"You've been invited to Coldstart as the admin for {company}.\n\n" +
            $"Set your password to get started:\n\n{setPasswordUrl}\n\n" +
            "This link expires in 7 days. If you weren't expecting this, ignore this email.",
        HtmlBody: Shell(
            heading: "You're invited to Coldstart",
            introHtml: $"You've been invited to Coldstart as the admin for <strong>{Enc(company)}</strong>. Set your password to get started.",
            ctaText: "Set your password",
            ctaUrl: setPasswordUrl,
            footnote: "This link expires in 7 days. If you weren't expecting this, you can ignore this email."));

    internal static EmailContent PasswordReset(string setPasswordUrl) => new(
        Subject: "Reset your Coldstart password",
        TextBody:
            $"Set a new password for your Coldstart account:\n\n{setPasswordUrl}\n\n" +
            "This link expires in 7 days. If you didn't ask for this, ignore this email.",
        HtmlBody: Shell(
            heading: "Reset your password",
            introHtml: "Use the button below to set a new password for your Coldstart account.",
            ctaText: "Set a new password",
            ctaUrl: setPasswordUrl,
            footnote: "This link expires in 7 days. If you didn't ask for this, you can ignore this email."));

    private static string Enc(string s) => WebUtility.HtmlEncode(s);

    private static string Shell(string heading, string introHtml, string ctaText, string ctaUrl, string footnote) => $$"""
<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
<body style="margin:0;padding:0;background:#F3E9D6;">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F3E9D6;">
    <tr><td align="center" style="padding:32px 16px;">
      <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;background:#FBF4E6;border:1px solid #E4D5BA;border-radius:14px;overflow:hidden;">
        <tr><td>
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>
            <td height="5" style="height:5px;background:#E07A3E;font-size:0;line-height:0;">&nbsp;</td>
            <td height="5" style="height:5px;background:#D9897F;font-size:0;line-height:0;">&nbsp;</td>
            <td height="5" style="height:5px;background:#E0A93B;font-size:0;line-height:0;">&nbsp;</td>
            <td height="5" style="height:5px;background:#5E9E94;font-size:0;line-height:0;">&nbsp;</td>
            <td height="5" style="height:5px;background:#6E6AB0;font-size:0;line-height:0;">&nbsp;</td>
          </tr></table>
        </td></tr>
        <tr><td style="padding:28px 32px 4px;">
          <span style="display:inline-block;width:34px;height:34px;background:#E07A3E;border-radius:9px;color:#FBF4E6;font-family:Consolas,Menlo,monospace;font-weight:800;font-size:20px;text-align:center;line-height:34px;vertical-align:middle;">C</span>
          <span style="font-family:Consolas,Menlo,monospace;font-weight:800;font-size:20px;color:#382C23;vertical-align:middle;margin-left:8px;">Cold<span style="color:#A04E1E;">start</span></span>
        </td></tr>
        <tr><td style="padding:8px 32px 0;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <h1 style="margin:12px 0 10px;font-size:22px;color:#382C23;">{{heading}}</h1>
          <p style="margin:0 0 24px;font-size:16px;line-height:1.6;color:#675541;">{{introHtml}}</p>
        </td></tr>
        <tr><td style="padding:0 32px 8px;">
          <a href="{{ctaUrl}}" style="display:inline-block;background:#A04E1E;color:#ffffff;text-decoration:none;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;font-weight:700;font-size:16px;padding:14px 26px;border-radius:11px;">{{ctaText}} &rarr;</a>
        </td></tr>
        <tr><td style="padding:16px 32px 0;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <p style="margin:0;font-size:13px;line-height:1.5;color:#675541;">Or paste this link into your browser:<br><a href="{{ctaUrl}}" style="color:#A04E1E;word-break:break-all;">{{ctaUrl}}</a></p>
        </td></tr>
        <tr><td style="padding:20px 32px 28px;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <p style="margin:0;font-size:13px;color:#8a7a66;">{{footnote}}</p>
        </td></tr>
        <tr><td style="padding:18px 32px;background:#F3E9D6;border-top:1px solid #E4D5BA;font-family:Consolas,Menlo,monospace;">
          <p style="margin:0;font-size:12px;color:#675541;">Coldstart &middot; <a href="{{Site}}" style="color:#A04E1E;text-decoration:none;">getcoldstart.nl</a></p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>
""";
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter EmailTemplatesTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Web/Email/EmailTemplates.cs src/Coldstart.Web/Coldstart.Web.csproj tests/Coldstart.Web.Tests/EmailTemplatesTests.cs
git commit -m "feat(email): branded HTML shell + invitation/reset templates"
```

---

### Task 3: Wire invitation + reset handlers to the branded templates

**Files:**
- Modify: `src/Coldstart.Web/Areas/Identity/Pages/Account/ForgotPassword.cshtml.cs`
- Modify: `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml.cs` (invite handler's send call)
- Modify: `tests/Coldstart.Web.Tests/PasswordResetTests.cs`, `tests/Coldstart.Web.Tests/AdminInviteTests.cs` (assert HTML)

**Interfaces:**
- Consumes: `EmailTemplates.Invitation`, `EmailTemplates.PasswordReset` (Task 2); `IEmailSender.SendAsync(...textBody, htmlBody, ct)` (Task 1).

- [ ] **Step 1: Strengthen the tests to require branded HTML**

In `tests/Coldstart.Web.Tests/PasswordResetTests.cs`, in `ForgotPassword_ForKnownUser_EmailsAResetLink`, add after the existing assertion:

```csharp
        factory.Email.Sent.Single().HtmlBody.Should().NotBeNull();
        factory.Email.Sent.Single().HtmlBody!.Should().Contain("/Identity/Account/ResetPassword").And.Contain("getcoldstart.nl");
```

In `tests/Coldstart.Web.Tests/AdminInviteTests.cs`, in `Invite_AsSuperAdmin_CreatesCompanyAdminWithTenant_AndEmailsLink`, add after the existing body assertion:

```csharp
        factory.Email.Sent.Single().HtmlBody!.Should().Contain("/Identity/Account/ResetPassword").And.Contain("Acme Co");
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter "PasswordResetTests|AdminInviteTests"`
Expected: FAIL — `HtmlBody` is null (handlers still send plain only).

- [ ] **Step 3: Update `ForgotPassword` to send the branded template**

In `src/Coldstart.Web/Areas/Identity/Pages/Account/ForgotPassword.cshtml.cs`, add `using Coldstart.Web.Email;` and replace the `await email.SendAsync(...)` block with:

```csharp
            var content = EmailTemplates.PasswordReset(link);
            await email.SendAsync(Input.Email, content.Subject, content.TextBody, content.HtmlBody, ct);
```

- [ ] **Step 4: Update the Admin invite handler to send the branded template**

In `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml.cs`, add `using Coldstart.Web.Email;` and replace the invite `await email.SendAsync(...)` block with:

```csharp
        var content = EmailTemplates.Invitation(Input.Company, link);
        await email.SendAsync(Input.Email, content.Subject, content.TextBody, content.HtmlBody, ct);
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter "PasswordResetTests|AdminInviteTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Web/Areas/Identity/Pages/Account/ForgotPassword.cshtml.cs src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml.cs tests/Coldstart.Web.Tests/PasswordResetTests.cs tests/Coldstart.Web.Tests/AdminInviteTests.cs
git commit -m "feat(email): send branded HTML invitation + password-reset emails"
```

---

### Task 4: `IDeleteCustomer` port + repository delete (tenant + its leads)

**Files:**
- Modify: `src/Coldstart.Application/Ports/ICustomerRepository.cs`
- Modify: `src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs`
- Create: `src/Coldstart.Application/Customers/IDeleteCustomer.cs`
- Create: `src/Coldstart.Application/Customers/DeleteCustomer.cs`
- Modify: `src/Coldstart.Application/DependencyInjection.cs`
- Test: `tests/Coldstart.Infrastructure.Tests/CustomerRepositoryTests.cs` (append)

**Interfaces:**
- Produces:
  - `ICustomerRepository.DeleteAsync(CustomerId id, CancellationToken ct)`
  - `IDeleteCustomer.DeleteAsync(Guid customerId, CancellationToken ct)` (+ `internal sealed class DeleteCustomer`)

- [ ] **Step 1: Write the failing test**

Append to `tests/Coldstart.Infrastructure.Tests/CustomerRepositoryTests.cs` (inside the class):

```csharp
    [Fact]
    public async Task DeleteAsync_RemovesCustomerAndItsLeads()
    {
        // Arrange
        await using var db = fixture.NewDbContext();
        var customers = new CustomerRepository(db);
        var leads = new LeadRepository(db);
        var customer = Customer.Create("delete-me@test.com");
        await customers.AddAsync(customer, CancellationToken.None);
        var lead = customer.AddLead("Lead Co", Fit.Yes, "subj", "draft", DateTimeOffset.UtcNow);
        await leads.AddAsync(lead, CancellationToken.None);

        // Act
        await customers.DeleteAsync(customer.Id, CancellationToken.None);

        // Assert
        await using var readDb = fixture.NewDbContext();
        var readCustomers = new CustomerRepository(readDb);
        var readLeads = new LeadRepository(readDb);
        (await readCustomers.GetAsync(customer.Id, CancellationToken.None)).Should().BeNull();
        (await readLeads.ForCustomerAsync(customer.Id, CancellationToken.None)).Should().BeEmpty();
    }
```

Add `using Coldstart.Infrastructure.Repositories;` if not already present (it is).

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests/Coldstart.Infrastructure.Tests.csproj --filter CustomerRepositoryTests`
Expected: FAIL — `DeleteAsync` not defined on `CustomerRepository` (compile error).

- [ ] **Step 3: Add `DeleteAsync` to the port**

In `src/Coldstart.Application/Ports/ICustomerRepository.cs`, add to the interface:

```csharp
    Task DeleteAsync(CustomerId id, CancellationToken ct);
```

- [ ] **Step 4: Implement it in the repository (delete leads, then customer)**

In `src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs`, add:

```csharp
    public async Task DeleteAsync(CustomerId id, CancellationToken ct)
    {
        var leads = await db.Leads.Where(l => l.CustomerId == id).ToListAsync(ct);
        db.Leads.RemoveRange(leads);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is not null) db.Customers.Remove(customer);
        await db.SaveChangesAsync(ct);
    }
```

- [ ] **Step 5: Add the Application port + adapter**

Create `src/Coldstart.Application/Customers/IDeleteCustomer.cs`:

```csharp
namespace Coldstart.Application.Customers;

public interface IDeleteCustomer
{
    Task DeleteAsync(Guid customerId, CancellationToken ct);
}
```

Create `src/Coldstart.Application/Customers/DeleteCustomer.cs`:

```csharp
using Coldstart.Application.Ports;
using Coldstart.Domain;

namespace Coldstart.Application.Customers;

internal sealed class DeleteCustomer(ICustomerRepository customers) : IDeleteCustomer
{
    public Task DeleteAsync(Guid customerId, CancellationToken ct) =>
        customers.DeleteAsync(new CustomerId(customerId), ct);
}
```

- [ ] **Step 6: Register in Application DI**

In `src/Coldstart.Application/DependencyInjection.cs`, add next to the other `Customers.*` registrations:

```csharp
        services.AddScoped<Customers.IDeleteCustomer, Customers.DeleteCustomer>();
```

- [ ] **Step 7: Run to verify pass**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests/Coldstart.Infrastructure.Tests.csproj --filter CustomerRepositoryTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Coldstart.Application/Ports/ICustomerRepository.cs src/Coldstart.Infrastructure/Repositories/CustomerRepository.cs src/Coldstart.Application/Customers/IDeleteCustomer.cs src/Coldstart.Application/Customers/DeleteCustomer.cs src/Coldstart.Application/DependencyInjection.cs tests/Coldstart.Infrastructure.Tests/CustomerRepositoryTests.cs
git commit -m "feat(customers): IDeleteCustomer removes a tenant and its leads"
```

---

### Task 5: Admin console — companies & users overview table

**Files:**
- Modify: `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml.cs`
- Modify: `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml`
- Test: `tests/Coldstart.Web.Tests/AdminConsoleTests.cs` (create)

**Interfaces:**
- Consumes: `ICustomerRepository.AllAsync` (existing), `ColdstartRoles`.
- Produces:
  - `IndexModel.Rows` → `IReadOnlyList<UserRow>` where `public sealed record UserRow(string Id, string Email, string Company, string Role, string Status, int? DaysLeft, bool CanManage)`
  - Renamed invite handler `OnPostInviteAsync` (handler name `Invite`).

- [ ] **Step 1: Write the failing test**

Create `tests/Coldstart.Web.Tests/AdminConsoleTests.cs`:

```csharp
using Coldstart.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Coldstart.Infrastructure.Identity;
using FluentAssertions;
using Xunit;

namespace Coldstart.Web.Tests;

public class AdminConsoleTests(ColdstartWebFactory factory) : IClassFixture<ColdstartWebFactory>
{
    [Fact]
    public async Task Overview_ListsInvitedAndActiveUsersWithStatus()
    {
        // Arrange — a SuperAdmin session, one pending invite, one active user
        var client = factory.CreateClient();
        var admin = $"boss-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, admin, "Passw0rd!", ColdstartRoles.SuperAdmin);

        var pending = $"pending-{Guid.NewGuid():N}@test.com";
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite",
            new() { ["Input.Company"] = "Pending Co", ["Input.Email"] = pending });

        var active = $"active-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, active, "Passw0rd!");   // EmailConfirmed=true => Active

        // Act
        var html = await client.GetStringAsync("/admin");

        // Assert
        html.Should().Contain(pending).And.Contain("Pending");
        html.Should().Contain(active).And.Contain("Active");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter AdminConsoleTests`
Expected: FAIL — the overview table + the `Invite` named handler don't exist yet (the invite POST 404s / no `active`+`Active` row rendered).

- [ ] **Step 3: Rework the page model — unified overview + named invite handler**

Replace `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml.cs` with (keeps invite behavior, renames the handler to `OnPostInviteAsync`, replaces Pending/Accepted with `Rows`):

```csharp
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Coldstart.Application.Customers;
using Coldstart.Application.Ports;
using Coldstart.Web.Email;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Web.Areas.Admin.Pages;

[Authorize(Roles = ColdstartRoles.SuperAdmin)]
public sealed class IndexModel(
    UserManager<ApplicationUser> users,
    IProvisionCustomer provision,
    ICustomerRepository customers,
    IEmailSender email) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? StatusMessage { get; private set; }
    public IReadOnlyList<UserRow> Rows { get; private set; } = [];

    public sealed record UserRow(string Id, string Email, string Company, string Role, string Status, int? DaysLeft, bool CanManage);

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostInviteAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }

        if (await users.FindByEmailAsync(Input.Email) is not null)
        {
            ModelState.AddModelError("Input.Email", "That person already has an account.");
            await LoadAsync(ct);
            return Page();
        }

        var user = new ApplicationUser
        {
            UserName = Input.Email,
            Email = Input.Email,
            EmailConfirmed = false,
            InvitedAt = DateTimeOffset.UtcNow,
        };
        var created = await users.CreateAsync(user, RandomPassword());
        if (!created.Succeeded)
        {
            foreach (var e in created.Errors) ModelState.AddModelError("", e.Description);
            await LoadAsync(ct);
            return Page();
        }

        await users.AddToRoleAsync(user, ColdstartRoles.CompanyAdmin);
        user.CustomerId = await provision.ProvisionAsync(Input.Company, ct);
        await users.UpdateAsync(user);

        await SendInviteAsync(user, Input.Company, ct);

        StatusMessage = $"Invitation sent to {Input.Email}.";
        Input = new InputModel();
        await LoadAsync(ct);
        return Page();
    }

    private async Task SendInviteAsync(ApplicationUser user, string company, CancellationToken ct)
    {
        var link = await BuildSetPasswordLinkAsync(user);
        var content = EmailTemplates.Invitation(company, link);
        await email.SendAsync(user.Email!, content.Subject, content.TextBody, content.HtmlBody, ct);
    }

    private async Task<string> BuildSetPasswordLinkAsync(ApplicationUser user)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        return Url.Page("/Account/ResetPassword", pageHandler: null,
            values: new { area = "Identity", code, email = user.Email }, protocol: Request.Scheme)!;
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var all = await customers.AllAsync(ct);
        var names = all.ToDictionary(c => c.Id.Value, c => c.Name);
        var currentEmail = User.Identity?.Name;

        var rows = new List<UserRow>();
        foreach (var u in await users.Users.ToListAsync(ct))
        {
            var roles = await users.GetRolesAsync(u);
            var role = roles.FirstOrDefault() ?? "";
            var isSuper = role == ColdstartRoles.SuperAdmin;
            var status = !u.EmailConfirmed ? "Pending"
                : u.LockoutEnd is { } le && le > DateTimeOffset.UtcNow ? "Blocked" : "Active";
            var company = u.CustomerId is { } cid && names.TryGetValue(cid, out var n) ? n : "—";
            rows.Add(new UserRow(u.Id, u.Email!, company, role, status,
                DaysLeft(u.InvitedAt), CanManage: !isSuper && !string.Equals(u.Email, currentEmail, StringComparison.OrdinalIgnoreCase)));
        }
        Rows = rows.OrderBy(r => r.Company).ThenBy(r => r.Email).ToList();
    }

    private static int? DaysLeft(DateTimeOffset? invitedAt) =>
        invitedAt is { } t ? Math.Max(0, 7 - (int)(DateTimeOffset.UtcNow - t).TotalDays) : null;

    private static string RandomPassword() =>
        "Aa1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    public sealed class InputModel
    {
        [Required] public string Company { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
    }
}
```

- [ ] **Step 4: Rework the page view — invite form (named handler) + overview table**

Replace `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml`:

```html
@page "/admin"
@model Coldstart.Web.Areas.Admin.Pages.IndexModel
@{ ViewData["Title"] = "Admin"; Layout = "/Pages/Shared/_Layout.cshtml"; }

<header class="app-bar">
  <a class="brand" href="/"><img src="/logo.svg" alt="Coldstart" style="height:30px;width:auto;display:block;"></a>
  <nav class="app-nav">
    <form method="post" asp-page="/Account/Logout" asp-area="Identity"><button class="btn btn-ghost" type="submit">Log out</button></form>
  </nav>
</header>

<main class="wrap app-zone">
  <h1>Company admins</h1>
  @if (Model.StatusMessage is not null) { <p class="form-status">@Model.StatusMessage</p> }

  <form method="post" asp-page-handler="Invite" class="admin-invite">
    <div asp-validation-summary="All" class="field-error"></div>
    <div class="field"><label asp-for="Input.Company">Company name</label><input asp-for="Input.Company" required></div>
    <div class="field"><label asp-for="Input.Email">Admin email</label><input asp-for="Input.Email" type="email" required></div>
    <button class="btn btn-primary" type="submit">Send invite →</button>
  </form>

  <h2 class="app-group-head">Companies &amp; users</h2>
  @if (Model.Rows.Count == 0) { <p class="app-group-empty">No accounts yet.</p> }
  else
  {
    <table class="admin-table">
      <thead><tr><th>Company</th><th>Email</th><th>Role</th><th>Status</th><th>Actions</th></tr></thead>
      <tbody>
      @foreach (var r in Model.Rows)
      {
        <tr>
          <td>@r.Company</td>
          <td>@r.Email</td>
          <td>@r.Role</td>
          <td>@r.Status@(r.Status == "Pending" && r.DaysLeft is not null ? $" · {r.DaysLeft}d" : "")</td>
          <td><!-- actions added in Tasks 6 & 7 --></td>
        </tr>
      }
      </tbody>
    </table>
  }
</main>
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter "AdminConsoleTests|AdminInviteTests"`
Expected: PASS. (Note: `AdminInviteTests` POSTs to `/admin` — the default handler no longer exists; if any of its POSTs 404, update those calls to `/admin?handler=Invite` in this step.)

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml.cs src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml tests/Coldstart.Web.Tests/AdminConsoleTests.cs tests/Coldstart.Web.Tests/AdminInviteTests.cs
git commit -m "feat(admin): unified companies & users overview table"
```

---

### Task 6: Admin actions — resend invite, revoke invite, copy link

**Files:**
- Modify: `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml.cs`
- Modify: `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml`
- Test: `tests/Coldstart.Web.Tests/AdminConsoleTests.cs` (append)

**Interfaces:**
- Consumes: `IDeleteCustomer.DeleteAsync` (Task 4), `EmailTemplates.Invitation` (Task 2), `BuildSetPasswordLinkAsync`/`SendInviteAsync` (Task 5).
- Produces: handlers `OnPostResendAsync(string email)`, `OnPostRevokeAsync(string email)`, `OnPostCopyLinkAsync(string email)`; `IndexModel.CopyLink` → `(string Email, string Url)?`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Coldstart.Web.Tests/AdminConsoleTests.cs` (add `using Microsoft.AspNetCore.Identity;` and `using Coldstart.Infrastructure.Identity;` if missing):

```csharp
    [Fact]
    public async Task Resend_ForPendingInvite_SendsAnotherInviteEmail()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
        var invitee = $"resend-{Guid.NewGuid():N}@test.com";
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite", new() { ["Input.Company"] = "Re Co", ["Input.Email"] = invitee });
        factory.Email.Sent.Clear();

        // Act
        await TestAuth.PostFormAsync(client, "/admin?handler=Resend", new() { ["email"] = invitee });

        // Assert
        factory.Email.Sent.Should().ContainSingle(m => m.To == invitee);
    }

    [Fact]
    public async Task Revoke_ForPendingInvite_DeletesTheAccountAndTenant()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
        var invitee = $"revoke-{Guid.NewGuid():N}@test.com";
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite", new() { ["Input.Company"] = "Rev Co", ["Input.Email"] = invitee });

        // Act
        await TestAuth.PostFormAsync(client, "/admin?handler=Revoke", new() { ["email"] = invitee });

        // Assert
        using var scope = factory.Services.CreateScope();
        var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await mgr.FindByEmailAsync(invitee)).Should().BeNull();
    }

    [Fact]
    public async Task CopyLink_ForPendingInvite_ShowsASetPasswordUrl()
    {
        // Arrange
        var client = factory.CreateClient();
        await TestAuth.SignInAsync(factory, client, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
        var invitee = $"copy-{Guid.NewGuid():N}@test.com";
        await TestAuth.PostFormAsync(client, "/admin?handler=Invite", new() { ["Input.Company"] = "Copy Co", ["Input.Email"] = invitee });

        // Act
        var html = await TestAuth.PostFormReadAsync(client, "/admin?handler=CopyLink", new() { ["email"] = invitee });

        // Assert
        html.Should().Contain("/Identity/Account/ResetPassword").And.Contain(System.Net.WebUtility.UrlEncode(invitee).Replace("%40", "%40"));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter AdminConsoleTests`
Expected: FAIL — the `Resend`/`Revoke`/`CopyLink` handlers 404.

- [ ] **Step 3: Add the handlers + copy-link state**

In `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml.cs`: add `IDeleteCustomer deleteCustomer` to the constructor parameter list (after `IEmailSender email`), add the `CopyLink` property, and add the three handlers.

Constructor becomes:

```csharp
public sealed class IndexModel(
    UserManager<ApplicationUser> users,
    IProvisionCustomer provision,
    ICustomerRepository customers,
    IDeleteCustomer deleteCustomer,
    IEmailSender email) : PageModel
```

Add after the `Rows` property:

```csharp
    public (string Email, string Url)? CopyLink { get; private set; }
```

Add these handlers (after `OnPostInviteAsync`):

```csharp
    public async Task<IActionResult> OnPostResendAsync(string email, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is { EmailConfirmed: false } && await IsCompanyAdminAsync(user))
        {
            var company = await CompanyNameAsync(user, ct);
            await SendInviteAsync(user, company, ct);
            StatusMessage = $"Invitation re-sent to {email}.";
        }
        else StatusMessage = "Nothing to resend for that account.";
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(string email, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is { EmailConfirmed: false } && await IsCompanyAdminAsync(user))
        {
            var customerId = user.CustomerId;
            await users.DeleteAsync(user);
            if (customerId is { } cid) await deleteCustomer.DeleteAsync(cid, ct);
            StatusMessage = $"Invitation to {email} revoked.";
        }
        else StatusMessage = "Only pending invites can be revoked.";
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCopyLinkAsync(string email, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is { EmailConfirmed: false })
            CopyLink = (email, await BuildSetPasswordLinkAsync(user));
        await LoadAsync(ct);
        return Page();
    }

    private async Task<bool> IsCompanyAdminAsync(ApplicationUser user) =>
        await users.IsInRoleAsync(user, ColdstartRoles.CompanyAdmin);

    private async Task<string> CompanyNameAsync(ApplicationUser user, CancellationToken ct)
    {
        if (user.CustomerId is not { } cid) return "your company";
        var all = await customers.AllAsync(ct);
        return all.FirstOrDefault(c => c.Id.Value == cid)?.Name ?? "your company";
    }
```

- [ ] **Step 4: Register `IDeleteCustomer` is already done in Task 4 — verify the page resolves**

No new DI needed (Task 4 registered `IDeleteCustomer`). This step is a build check only.

Run: `dotnet build src/Coldstart.Web -clp:ErrorsOnly`
Expected: 0 errors, 0 warnings.

- [ ] **Step 5: Add per-row pending actions + copy-link reveal to the view**

In `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml`, replace the actions cell (`<td><!-- actions added in Tasks 6 & 7 --></td>`) with:

```html
          <td class="admin-actions">
            @if (r.Status == "Pending")
            {
              <form method="post" asp-page-handler="Resend"><input type="hidden" name="email" value="@r.Email"><button class="btn btn-ghost btn-sm" type="submit">Resend</button></form>
              <form method="post" asp-page-handler="CopyLink"><input type="hidden" name="email" value="@r.Email"><button class="btn btn-ghost btn-sm" type="submit">Copy link</button></form>
              <form method="post" asp-page-handler="Revoke" onsubmit="return confirm('Revoke the invite for @r.Email?');"><input type="hidden" name="email" value="@r.Email"><button class="btn btn-ghost btn-sm" type="submit">Revoke</button></form>
            }
          </td>
```

And add, just below the `@if (Model.StatusMessage ...)` line near the top of `<main>`:

```html
  @if (Model.CopyLink is { } cl)
  {
    <div class="form-status">Set-password link for @cl.Email:<br><input class="copy-link" type="text" readonly value="@cl.Url" onclick="this.select()" style="width:100%;"></div>
  }
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter AdminConsoleTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml.cs src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml tests/Coldstart.Web.Tests/AdminConsoleTests.cs
git commit -m "feat(admin): resend, revoke, and copy-link actions for pending invites"
```

---

### Task 7: Admin actions — block, unblock, remove (with guards)

**Files:**
- Modify: `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml.cs`
- Modify: `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml`
- Test: `tests/Coldstart.Web.Tests/AdminConsoleTests.cs` (append)

**Interfaces:**
- Consumes: `IDeleteCustomer.DeleteAsync` (Task 4), `UserManager` lockout APIs.
- Produces: handlers `OnPostBlockAsync(string id)`, `OnPostUnblockAsync(string id)`, `OnPostRemoveAsync(string id)`; guard helper `GuardTargetAsync`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Coldstart.Web.Tests/AdminConsoleTests.cs`:

```csharp
    [Fact]
    public async Task Block_ThenLogin_IsRejected_AndUnblock_Restores()
    {
        // Arrange — an active user with a known password + tenant
        var admin = factory.CreateClient(new() { AllowAutoRedirect = false });
        await TestAuth.SignInAsync(factory, admin, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
        var target = $"blockme-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, target, "Passw0rd!");
        string userId;
        using (var scope = factory.Services.CreateScope())
        {
            var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            userId = (await mgr.FindByEmailAsync(target))!.Id;
        }

        // Act — block, then attempt to log in on a fresh client
        await TestAuth.PostFormAsync(admin, "/admin?handler=Block", new() { ["id"] = userId });
        var blocked = factory.CreateClient(new() { AllowAutoRedirect = false });
        await TestAuth.PostFormAsync(blocked, "/Identity/Account/Login",
            new() { ["Input.Email"] = target, ["Input.Password"] = "Passw0rd!", ["Input.RememberMe"] = "false" });
        var appWhileBlocked = await blocked.GetAsync("/app");

        // Assert — blocked user is not authenticated (bounced to login)
        appWhileBlocked.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        appWhileBlocked.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");

        // Act — unblock, then log in again
        await TestAuth.PostFormAsync(admin, "/admin?handler=Unblock", new() { ["id"] = userId });
        var ok = factory.CreateClient(new() { AllowAutoRedirect = false });
        await TestAuth.PostFormAsync(ok, "/Identity/Account/Login",
            new() { ["Input.Email"] = target, ["Input.Password"] = "Passw0rd!", ["Input.RememberMe"] = "false" });
        var appAfterUnblock = await ok.GetAsync("/app");

        // Assert — now authenticated (200, not redirected to login)
        appAfterUnblock.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Remove_DeletesUserAndTenant()
    {
        // Arrange
        var admin = factory.CreateClient();
        await TestAuth.SignInAsync(factory, admin, $"boss-{Guid.NewGuid():N}@test.com", "Passw0rd!", ColdstartRoles.SuperAdmin);
        var target = $"removeme-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, target, "Passw0rd!");
        string userId;
        using (var scope = factory.Services.CreateScope())
        {
            var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            userId = (await mgr.FindByEmailAsync(target))!.Id;
        }

        // Act
        await TestAuth.PostFormAsync(admin, "/admin?handler=Remove", new() { ["id"] = userId });

        // Assert
        using var scope2 = factory.Services.CreateScope();
        var users = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.FindByEmailAsync(target)).Should().BeNull();
    }

    [Fact]
    public async Task Remove_SuperAdmin_IsRejected()
    {
        // Arrange
        var admin = factory.CreateClient();
        var bossEmail = $"boss-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, admin, bossEmail, "Passw0rd!", ColdstartRoles.SuperAdmin);
        string bossId;
        using (var scope = factory.Services.CreateScope())
        {
            var mgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            bossId = (await mgr.FindByEmailAsync(bossEmail))!.Id;
        }

        // Act — try to remove self (a SuperAdmin)
        await TestAuth.PostFormAsync(admin, "/admin?handler=Remove", new() { ["id"] = bossId });

        // Assert — still there
        using var scope2 = factory.Services.CreateScope();
        var users = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.FindByEmailAsync(bossEmail)).Should().NotBeNull();
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter AdminConsoleTests`
Expected: FAIL — `Block`/`Unblock`/`Remove` handlers 404.

- [ ] **Step 3: Add the handlers + guard**

In `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml.cs`, add these handlers (after `OnPostCopyLinkAsync`):

```csharp
    public async Task<IActionResult> OnPostBlockAsync(string id, CancellationToken ct)
    {
        var user = await GuardTargetAsync(id);
        if (user is not null)
        {
            await users.SetLockoutEnabledAsync(user, true);
            await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
            await users.UpdateSecurityStampAsync(user);   // invalidate any live cookie
            StatusMessage = $"{user.Email} is blocked.";
        }
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostUnblockAsync(string id, CancellationToken ct)
    {
        var user = await GuardTargetAsync(id);
        if (user is not null)
        {
            await users.SetLockoutEndDateAsync(user, null);
            StatusMessage = $"{user.Email} is active again.";
        }
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveAsync(string id, CancellationToken ct)
    {
        var user = await GuardTargetAsync(id);
        if (user is not null)
        {
            var customerId = user.CustomerId;
            await users.DeleteAsync(user);
            if (customerId is { } cid) await deleteCustomer.DeleteAsync(cid, ct);
            StatusMessage = "Account removed.";
        }
        await LoadAsync(ct);
        return Page();
    }

    // Returns the target only if it is safe to act on: exists, is not a SuperAdmin, and is not the current user.
    private async Task<ApplicationUser?> GuardTargetAsync(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) { StatusMessage = "No such account."; return null; }
        if (await users.IsInRoleAsync(user, ColdstartRoles.SuperAdmin)
            || string.Equals(user.Email, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "That account can't be modified.";
            return null;
        }
        return user;
    }
```

- [ ] **Step 4: Add per-row active-user actions to the view**

In `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml`, inside the actions `<td>`, add an else-branch for manageable non-pending rows (after the existing `@if (r.Status == "Pending") { ... }`):

```html
            @if (r.Status != "Pending" && r.CanManage)
            {
              @if (r.Status == "Blocked")
              {
                <form method="post" asp-page-handler="Unblock"><input type="hidden" name="id" value="@r.Id"><button class="btn btn-ghost btn-sm" type="submit">Unblock</button></form>
              }
              else
              {
                <form method="post" asp-page-handler="Block"><input type="hidden" name="id" value="@r.Id"><button class="btn btn-ghost btn-sm" type="submit">Block</button></form>
              }
              <form method="post" asp-page-handler="Remove" onsubmit="return confirm('Remove @r.Email and delete their data? This cannot be undone.');"><input type="hidden" name="id" value="@r.Id"><button class="btn btn-ghost btn-sm" type="submit">Remove</button></form>
            }
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/Coldstart.Web.Tests/Coldstart.Web.Tests.csproj --filter AdminConsoleTests`
Expected: PASS.

- [ ] **Step 6: Full build + suite**

Run: `dotnet build Coldstart.sln -clp:ErrorsOnly` then `dotnet test Coldstart.sln`
Expected: 0 warnings; all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml.cs src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml tests/Coldstart.Web.Tests/AdminConsoleTests.cs
git commit -m "feat(admin): block/unblock and remove accounts with SuperAdmin/self guards"
```

---

## Self-Review

**Spec coverage:**
- Admin console: overview table → Task 5; resend/revoke/copy-link → Task 6; block/unblock/remove + guards → Task 7. ✔
- Branded email system: transport (multipart) → Task 1; shell + templates → Task 2; wire invite/reset → Task 3. ✔
- `IDeleteCustomer` (Application port + Infra adapter, no DbContext in Web) → Task 4, consumed in Tasks 6/7. ✔
- No migration (block via lockout, remove via delete) → Tasks 4/7. ✔
- Testing: `FakeEmailSender` captures HTML (Task 1); template unit tests (Task 2); admin action tests (5/6/7); repo delete test (Task 4). ✔
- Deferred (2 cold-outreach templates, profile editing, 2FA) → not in plan, per spec. ✔

**Placeholder scan:** none — every step carries real code/commands. The only intentional "added later" marker is the actions-cell comment in Task 5, filled in Tasks 6 and 7.

**Type consistency:** `IEmailSender.SendAsync(to, subject, textBody, htmlBody, ct)` used verbatim in Tasks 1/3/5. `EmailContent(Subject, TextBody, HtmlBody)` + `EmailTemplates.Invitation/PasswordReset` consistent across 2/3/5. `IDeleteCustomer.DeleteAsync(Guid, ct)` (Task 4) consumed in 6/7. `ICustomerRepository.DeleteAsync(CustomerId, ct)` (Task 4). `UserRow(Id, Email, Company, Role, Status, DaysLeft, CanManage)` defined in Task 5, consumed by the view in 5/6/7. Named handlers `Invite`/`Resend`/`Revoke`/`CopyLink`/`Block`/`Unblock`/`Remove` match `asp-page-handler` values and `?handler=` test calls. `FakeEmailSender.Sent` tuple `(To, Subject, TextBody, HtmlBody)` consistent from Task 1 onward.

**Ordering guard:** Task 1 (transport) precedes Task 3 (which sends html). Task 2 (templates) precedes Task 3/5. Task 4 (`IDeleteCustomer`) precedes Tasks 6 (revoke) and 7 (remove). Task 5 (overview + `IDeleteCustomer` ctor injection happens in Task 6) — note Task 6 Step 3 adds `deleteCustomer` to the constructor; Task 5 does not reference it, so Task 5 compiles standalone.

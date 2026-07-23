# Send from Coldstart Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An admin-only "Send from Coldstart" action on the lead detail that sends the approved draft as a real, deliverability-safe branded email from `andrzej@getcoldstart.nl`, then marks the lead Sent.

**Architecture:** New outbound adapter (`IOutreachMailer` port + `SmtpOutreachMailer` MailKit impl + `OutreachMailboxOptions`). New async use case `ILeadWorkflow.SendFromColdstartAsync`. New admin-gated Razor handler + a button in the existing `approved` send panel. Per `docs/superpowers/specs/2026-07-23-send-from-coldstart-design.md`.

**Tech Stack:** .NET 10, hexagonal (Domain/Application/Infrastructure/Web), MailKit/MimeKit, EF Core + Postgres, xUnit + FluentAssertions + NSubstitute, WebApplicationFactory + Testcontainers.

## Global Constraints

- Hexagonal: the port `IOutreachMailer` lives in **Application**; the MailKit impl + options live in **Infrastructure**. Application never references MimeKit/MailKit types.
- Sending identity is the **`OutreachMailbox`** config section (`andrzej@getcoldstart.nl`); secrets live in user-secrets/env, never in the repo. SMTP: STARTTLS on 587, `CheckCertificateRevocation = false` (same CRL workaround as `SmtpEmailSender`).
- **Deliverability-safe branding:** multipart/alternative (text + html); the HTML signature is **CSS/text only — no `<img>`**. Body stays plain and personal.
- Admin gate: the send handler and its button are **SuperAdmin-only** (`ColdstartRoles.SuperAdmin` / `User.IsInRole("SuperAdmin")`). Customers keep the Gmail/mailto/copy handoff and see no new button.
- Send is attempted **before** `MarkSent`; a failed send returns `Invalid` and leaves the lead Approved (never Sent).
- Async all the way with `CancellationToken`; `internal` impls; `record` for the DTO. Conventional commits, **no `Co-Authored-By` / "Generated with" trailers**. Stage only the listed paths.
- Full-solution `dotnet test` green before committing each task.

---

### Task 1: Outreach outbound adapter (port + options + SMTP impl + wiring)

**Files:**
- Create: `src/Coldstart.Application/Outreach/IOutreachMailer.cs`
- Create: `src/Coldstart.Infrastructure/Outreach/OutreachMailboxOptions.cs`
- Create: `src/Coldstart.Infrastructure/Outreach/SmtpOutreachMailer.cs`
- Modify: `src/Coldstart.Infrastructure/DependencyInjection.cs` (register `IOutreachMailer`)
- Modify: `src/Coldstart.Web/Program.cs` (bind `OutreachMailbox` section)
- Test: `tests/Coldstart.Infrastructure.Tests/SmtpOutreachMailerTests.cs`

**Interfaces:**
- Produces: `IOutreachMailer.SendAsync(OutreachEmail, CancellationToken)`; `OutreachEmail(string To, string Subject, string Body)`; `SmtpOutreachMailer.BuildMessage(OutreachMailboxOptions, OutreachEmail)` (internal static test seam).

- [ ] **Step 1: Write the port + DTO**

Create `src/Coldstart.Application/Outreach/IOutreachMailer.cs`:

```csharp
namespace Coldstart.Application.Outreach;

// Outbound port: sends a single cold-outreach email from the operator's Coldstart identity.
// The implementation brands the body (deliverability-safe: CSS signature, no images) and throws on failure.
public interface IOutreachMailer
{
    Task SendAsync(OutreachEmail mail, CancellationToken ct);
}

// Body is the approved plain-text draft; the adapter builds the text + branded-HTML alternatives.
public sealed record OutreachEmail(string To, string Subject, string Body);
```

- [ ] **Step 2: Write the options type**

Create `src/Coldstart.Infrastructure/Outreach/OutreachMailboxOptions.cs`:

```csharp
namespace Coldstart.Infrastructure.Outreach;

public sealed class OutreachMailboxOptions
{
    public string Host { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseStartTls { get; set; } = true; // false only for plaintext test servers
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromName { get; set; } = "Andrzej — Coldstart";
}
```

- [ ] **Step 3: Write the failing message-composition tests**

Create `tests/Coldstart.Infrastructure.Tests/SmtpOutreachMailerTests.cs`:

```csharp
using Coldstart.Application.Outreach;
using Coldstart.Infrastructure.Outreach;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class SmtpOutreachMailerTests
{
    private static OutreachMailboxOptions Opts() => new()
    {
        Host = "mail.example.test", Username = "andrzej@getcoldstart.nl",
        Password = "x", FromName = "Andrzej — Coldstart",
    };

    [Fact]
    public void BuildMessage_SetsEnvelope_FromConfiguredIdentity()
    {
        // Arrange
        var mail = new OutreachEmail("ceo@acme.com", "About your $15M raise", "First line.\n\nSecond line.");

        // Act
        var msg = SmtpOutreachMailer.BuildMessage(Opts(), mail);

        // Assert
        msg.Subject.Should().Be("About your $15M raise");
        msg.To.ToString().Should().Contain("ceo@acme.com");
        msg.From.ToString().Should().Contain("andrzej@getcoldstart.nl");
    }

    [Fact]
    public void BuildMessage_BrandsHtml_WithCssSignature_AndNoImages()
    {
        // Arrange
        var mail = new OutreachEmail("ceo@acme.com", "s", "Hello there.");

        // Act
        var msg = SmtpOutreachMailer.BuildMessage(Opts(), mail);

        // Assert — deliverability-safe: branded via text/CSS, never an <img>
        msg.HtmlBody.Should().NotBeNull();
        msg.HtmlBody.Should().Contain("coldstart");            // wordmark in the signature
        msg.HtmlBody.Should().Contain("Founder, Coldstart");
        msg.HtmlBody.Should().NotContain("<img");
        msg.HtmlBody.Should().Contain("Hello there.");         // the body is present
    }

    [Fact]
    public void BuildMessage_IncludesPlainTextAlternative_WithBodyAndPlainSignature()
    {
        // Arrange
        var mail = new OutreachEmail("ceo@acme.com", "s", "Body copy here.");

        // Act
        var msg = SmtpOutreachMailer.BuildMessage(Opts(), mail);

        // Assert
        msg.TextBody.Should().NotBeNull();
        msg.TextBody.Should().Contain("Body copy here.");
        msg.TextBody.Should().Contain("Founder, Coldstart");
        msg.TextBody.Should().NotContain("<");                 // plain part carries no markup
    }
}
```

- [ ] **Step 4: Run to verify failure**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~SmtpOutreachMailer"`
Expected: compile failure / FAIL — `SmtpOutreachMailer` does not exist yet.

- [ ] **Step 5: Write `SmtpOutreachMailer`**

Create `src/Coldstart.Infrastructure/Outreach/SmtpOutreachMailer.cs`:

```csharp
using System.Net;
using System.Text;
using Coldstart.Application.Outreach;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Coldstart.Infrastructure.Outreach;

internal sealed class SmtpOutreachMailer(IOptions<OutreachMailboxOptions> options) : IOutreachMailer
{
    private readonly OutreachMailboxOptions _o = options.Value;

    public async Task SendAsync(OutreachEmail mail, CancellationToken ct)
    {
        var msg = BuildMessage(_o, mail);

        using var client = new SmtpClient();
        // CRL/OCSP endpoints are often unreachable (macOS + .NET report "incomplete certificate
        // revocation check") which MailKit treats as fatal; chain + hostname are still validated.
        client.CheckCertificateRevocation = false;
        await client.ConnectAsync(_o.Host, _o.SmtpPort,
            _o.SmtpUseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
        await client.AuthenticateAsync(_o.Username, _o.Password, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }

    // Deliverability-safe branding: personal plain body + a CSS-only Coldstart signature. No images.
    internal static MimeMessage BuildMessage(OutreachMailboxOptions o, OutreachEmail mail)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(o.FromName, o.Username));
        msg.To.Add(MailboxAddress.Parse(mail.To));
        msg.Subject = mail.Subject;

        var body = mail.Body ?? "";
        msg.Body = new BodyBuilder
        {
            TextBody = body + "\n\n— Andrzej\nFounder, Coldstart\nhttps://getcoldstart.nl",
            HtmlBody = BuildHtml(body),
        }.ToMessageBody();
        return msg;
    }

    private static string BuildHtml(string body)
    {
        var paragraphs = new StringBuilder();
        foreach (var para in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            paragraphs.Append("<p style=\"margin:0 0 12px\">")
                      .Append(WebUtility.HtmlEncode(para).Replace("\n", "<br>"))
                      .Append("</p>");

        // CSS/text wordmark — the '*' is the brand mark, tinted sunrise; no external assets.
        return $"""
        <div style="font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;font-size:15px;line-height:1.5;color:#16192B">
          {paragraphs}
          <div style="margin-top:20px;padding-top:12px;border-top:1px solid #E7E2D8;color:#16192B">
            <div style="font-weight:700;letter-spacing:-0.01em">coldstart<span style="color:#FF6A3D">*</span></div>
            <div style="font-size:13px;color:#5A5E70">Andrzej · Founder, Coldstart · <a href="https://getcoldstart.nl" style="color:#FF6A3D;text-decoration:none">getcoldstart.nl</a></div>
          </div>
        </div>
        """;
    }
}
```

- [ ] **Step 6: Register the service + bind the options**

In `src/Coldstart.Infrastructure/DependencyInjection.cs`, add next to the other `AddScoped` port registrations (near line 17-18):

```csharp
        services.AddScoped<Application.Outreach.IOutreachMailer, Outreach.SmtpOutreachMailer>();
```

In `src/Coldstart.Web/Program.cs`, add after the `Llm` options binding (line 19):

```csharp
builder.Services.Configure<Coldstart.Infrastructure.Outreach.OutreachMailboxOptions>(builder.Configuration.GetSection("OutreachMailbox"));
```

- [ ] **Step 7: Run tests**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~SmtpOutreachMailer"` → PASS.
Then full `dotnet test` → green.

- [ ] **Step 8: Commit**

```bash
git add src/Coldstart.Application/Outreach/IOutreachMailer.cs src/Coldstart.Infrastructure/Outreach/OutreachMailboxOptions.cs src/Coldstart.Infrastructure/Outreach/SmtpOutreachMailer.cs src/Coldstart.Infrastructure/DependencyInjection.cs src/Coldstart.Web/Program.cs tests/Coldstart.Infrastructure.Tests/SmtpOutreachMailerTests.cs
git commit -m "feat(outreach): branded, deliverability-safe outreach mailer (andrzej@ SMTP identity)"
```

---

### Task 2: `SendFromColdstartAsync` use case

**Files:**
- Modify: `src/Coldstart.Application/Leads/ILeadWorkflow.cs` (add method)
- Modify: `src/Coldstart.Application/Leads/LeadWorkflow.cs` (implement; inject `IOutreachMailer`)
- Test: `tests/Coldstart.Application.Tests/LeadWorkflowSendTests.cs`

**Interfaces:**
- Consumes: `IOutreachMailer` (Task 1), `ILeadRepository`, `Lead` domain (`Status`, `RecipientEmail`, `Subject`, `Draft`, `MarkSent`).
- Produces: `ILeadWorkflow.SendFromColdstartAsync(Guid customerId, Guid leadId, CancellationToken ct) : Task<LeadActionResult>`.

- [ ] **Step 1: Add the interface method**

In `src/Coldstart.Application/Leads/ILeadWorkflow.cs`, add after `MarkSentAsync`:

```csharp
    Task<LeadActionResult> SendFromColdstartAsync(Guid customerId, Guid leadId, CancellationToken ct);
```

- [ ] **Step 2: Write the failing use-case tests**

Create `tests/Coldstart.Application.Tests/LeadWorkflowSendTests.cs`:

```csharp
using Coldstart.Application.Leads;
using Coldstart.Application.Outreach;
using Coldstart.Application.Ports;
using Coldstart.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Coldstart.Application.Tests;

public class LeadWorkflowSendTests
{
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static Lead ApprovedLead(string recipient = "ceo@acme.com")
    {
        var lead = Lead.CreateHarvested(new CustomerId(CustomerId), "Acme", Fit.Yes,
            "About your raise", "Body copy.", "hn", "https://x/1", "fp1", 0.7, "outbound", DateTimeOffset.UtcNow);
        lead.Approve(new EmailAddress(recipient));
        return lead;
    }

    private static (LeadWorkflow sut, ILeadRepository repo, IOutreachMailer mailer) Sut(Lead? lead)
    {
        var repo = Substitute.For<ILeadRepository>();
        if (lead is not null) repo.GetAsync(Arg.Any<LeadId>(), Arg.Any<CancellationToken>()).Returns(lead);
        var mailer = Substitute.For<IOutreachMailer>();
        return (new LeadWorkflow(repo, mailer), repo, mailer);
    }

    [Fact]
    public async Task SendFromColdstartAsync_WhenApprovedWithRecipient_SendsStoredContentAndMarksSent()
    {
        // Arrange
        var lead = ApprovedLead();
        var (sut, repo, mailer) = Sut(lead);

        // Act
        var result = await sut.SendFromColdstartAsync(CustomerId, lead.Id.Value, CancellationToken.None);

        // Assert
        result.IsOk.Should().BeTrue();
        await mailer.Received(1).SendAsync(
            Arg.Is<OutreachEmail>(m => m.To == "ceo@acme.com" && m.Subject == "About your raise" && m.Body == "Body copy."),
            Arg.Any<CancellationToken>());
        lead.Status.Should().Be(LeadStatus.Sent);
        lead.SentAt.Should().NotBeNull();
        await repo.Received().UpdateAsync(lead, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendFromColdstartAsync_WhenNotApproved_ReturnsInvalid_AndDoesNotSend()
    {
        // Arrange — a pending lead
        var lead = Lead.CreateHarvested(new CustomerId(CustomerId), "Acme", Fit.Yes, "s", "b",
            "hn", "https://x/1", "fp2", 0.7, "outbound", DateTimeOffset.UtcNow);
        var (sut, _, mailer) = Sut(lead);

        // Act
        var result = await sut.SendFromColdstartAsync(CustomerId, lead.Id.Value, CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.Invalid);
        lead.Status.Should().Be(LeadStatus.PendingReview);
        await mailer.DidNotReceive().SendAsync(Arg.Any<OutreachEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendFromColdstartAsync_WhenSendThrows_ReturnsInvalid_AndLeaves Lead Approved()
    {
        // Arrange
        var lead = ApprovedLead();
        var (sut, repo, mailer) = Sut(lead);
        mailer.SendAsync(Arg.Any<OutreachEmail>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("smtp down"));

        // Act
        var result = await sut.SendFromColdstartAsync(CustomerId, lead.Id.Value, CancellationToken.None);

        // Assert — failed send never marks Sent
        result.Status.Should().Be(LeadActionStatus.Invalid);
        lead.Status.Should().Be(LeadStatus.Approved);
        lead.SentAt.Should().BeNull();
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Lead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendFromColdstartAsync_WhenLeadNotOwned_ReturnsNotFound()
    {
        // Arrange — repo returns null (not found / not this customer)
        var (sut, _, mailer) = Sut(lead: null);

        // Act
        var result = await sut.SendFromColdstartAsync(CustomerId, Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(LeadActionStatus.NotFound);
        await mailer.DidNotReceive().SendAsync(Arg.Any<OutreachEmail>(), Arg.Any<CancellationToken>());
    }
}
```

> Note: rename the third test method to a valid identifier `SendFromColdstartAsync_WhenSendThrows_ReturnsInvalid_AndLeavesLeadApproved` (no spaces) when writing the file.

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/Coldstart.Application.Tests --filter "FullyQualifiedName~LeadWorkflowSend"`
Expected: compile failure — `LeadWorkflow` has no `IOutreachMailer` ctor param / no `SendFromColdstartAsync`.

- [ ] **Step 4: Implement**

In `src/Coldstart.Application/Leads/LeadWorkflow.cs`, change the primary constructor to inject the mailer:

```csharp
internal sealed class LeadWorkflow(ILeadRepository leads, Outreach.IOutreachMailer outreach) : ILeadWorkflow
```

(add `using Coldstart.Application.Outreach;` or reference via `Outreach.IOutreachMailer` — match the file's existing using style). Then add the method (it does its own load/ownership/persist because the send is async and cannot run inside the sync `MutateAsync` seam):

```csharp
    public async Task<LeadActionResult> SendFromColdstartAsync(Guid customerId, Guid leadId, CancellationToken ct)
    {
        var lead = await leads.GetAsync(new LeadId(leadId), ct);
        if (lead is null || lead.CustomerId.Value != customerId)
            return LeadActionResult.NotFound;
        if (lead.Status != LeadStatus.Approved)
            return LeadActionResult.Invalid("Approve the lead before sending from Coldstart.");
        if (lead.RecipientEmail is null)
            return LeadActionResult.Invalid("A recipient is required before sending.");

        try
        {
            await outreach.SendAsync(new OutreachEmail(lead.RecipientEmail.Value, lead.Subject, lead.Draft), ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return LeadActionResult.Invalid("Couldn't send just now — the email wasn't sent. Please try again.");
        }

        lead.MarkSent(DateTimeOffset.UtcNow);
        await leads.UpdateAsync(lead, ct);
        return LeadActionResult.Ok;
    }
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/Coldstart.Application.Tests --filter "FullyQualifiedName~LeadWorkflowSend"` → PASS.
Then full `dotnet test` → green (all existing `LeadWorkflow` consumers still compile; the ctor gained a param — the DI container and tests that build `LeadWorkflow` directly must pass a mailer; the app resolves it via DI automatically).

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Application/Leads/ILeadWorkflow.cs src/Coldstart.Application/Leads/LeadWorkflow.cs tests/Coldstart.Application.Tests/LeadWorkflowSendTests.cs
git commit -m "feat(leads): SendFromColdstartAsync — send approved draft, then mark sent (send failure never marks sent)"
```

---

### Task 3: Admin-gated Razor handler + UI button

**Files:**
- Modify: `src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml.cs` (handler)
- Modify: `src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml` (button)
- Modify: `tests/Coldstart.Web.Tests/ColdstartWebFactory.cs` (register `FakeOutreachMailer`)
- Test: `tests/Coldstart.Web.Tests/LeadDetailTests.cs` (add admin-send tests)

**Interfaces:**
- Consumes: `ILeadWorkflow.SendFromColdstartAsync` (Task 2), `ColdstartRoles.SuperAdmin`.
- Produces: handler `OnPostSendFromColdstartAsync`; fake `ColdstartWebFactory.Outreach`.

- [ ] **Step 1: Add a fake mailer to the test factory**

In `tests/Coldstart.Web.Tests/ColdstartWebFactory.cs`, add a property beside the other fakes:

```csharp
    public FakeOutreachMailer Outreach { get; } = new();
```

In `ConfigureServices`, add beside the other `RemoveAll`/`AddSingleton` swaps:

```csharp
            services.RemoveAll<Coldstart.Application.Outreach.IOutreachMailer>();
            services.AddSingleton<Coldstart.Application.Outreach.IOutreachMailer>(Outreach);
```

At the bottom of the file (next to `FakeCompanyBriefPolisher`), add:

```csharp
// Fake IOutreachMailer: records the last sent email; can be told to throw to simulate SMTP failure.
public sealed class FakeOutreachMailer : Coldstart.Application.Outreach.IOutreachMailer
{
    public Coldstart.Application.Outreach.OutreachEmail? LastSent { get; private set; }
    public bool Throw { get; set; }

    public Task SendAsync(Coldstart.Application.Outreach.OutreachEmail mail, CancellationToken ct)
    {
        if (Throw) throw new InvalidOperationException("simulated smtp failure");
        LastSent = mail;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Write the failing page tests**

In `tests/Coldstart.Web.Tests/LeadDetailTests.cs`, add these tests (they reuse the existing private `AddApprovedLeadWithRecipientAsync` helper):

```csharp
    [Fact]
    public async Task Detail_ForAdmin_ShowsSendFromColdstart_AndSendingMarksSent()
    {
        // Arrange — an account that is BOTH SuperAdmin and has a tenant (role added before sign-in
        // so it lands in the auth cookie), with an approved lead.
        var client = factory.CreateClient();
        var email = $"admin-send-{Guid.NewGuid():N}@test.com";
        await TestAuth.CreateUserAsync(factory, email, "Passw0rd!", role: ColdstartRoles.SuperAdmin, provisionTenant: true);
        await TestAuth.PostFormAsync(client, "/Identity/Account/Login", new()
        {
            ["Input.Email"] = email, ["Input.Password"] = "Passw0rd!", ["Input.RememberMe"] = "false",
        });
        var id = await AddApprovedLeadWithRecipientAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"), "ceo@acme.com");
        factory.Outreach.LastSent = null;

        // Act
        var page = await client.GetStringAsync($"/app/leads/{id}");
        var form = await AntiforgeryForm(client, $"/app/leads/{id}", new());
        var post = await client.PostAsync($"/app/leads/{id}?handler=SendFromColdstart", new FormUrlEncodedContent(form));

        // Assert — admin sees the button; posting sends via the mailer and marks the lead Sent
        page.Should().Contain("SendFromColdstart");
        post.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
        factory.Outreach.LastSent.Should().NotBeNull();
        factory.Outreach.LastSent!.To.Should().Be("ceo@acme.com");
        var sent = await client.GetStringAsync("/app?tab=sent");
        sent.Should().Contain("Acme Co");
    }

    [Fact]
    public async Task Detail_ForRegularCustomer_HidesSendFromColdstart_AndForbidsThePost()
    {
        // Arrange — a normal customer (no SuperAdmin role)
        var client = factory.CreateClient();
        var email = $"cust-send-{Guid.NewGuid():N}@test.com";
        await TestAuth.SignInAsync(factory, client, email, "Passw0rd!");
        var id = await AddApprovedLeadWithRecipientAsync(email, "Acme Co", "fp-" + Guid.NewGuid().ToString("N"), "ceo@acme.com");
        factory.Outreach.LastSent = null;

        // Act
        var page = await client.GetStringAsync($"/app/leads/{id}");
        var form = await AntiforgeryForm(client, $"/app/leads/{id}", new());
        var post = await client.PostAsync($"/app/leads/{id}?handler=SendFromColdstart", new FormUrlEncodedContent(form));

        // Assert — no button, and the handler refuses non-admins
        page.Should().NotContain("SendFromColdstart");
        post.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.Outreach.LastSent.Should().BeNull();
    }
```

Add `using Coldstart.Infrastructure.Identity;` to the test file if `ColdstartRoles` is not already imported (it lives in that namespace; verify and add the correct using).

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~SendFromColdstart"`
Expected: FAIL — no handler (`SendFromColdstart` post 404/405, button absent).

- [ ] **Step 4: Add the handler**

In `src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml.cs`, add after `OnPostMarkSentAsync`:

```csharp
    public async Task<IActionResult> OnPostSendFromColdstartAsync(Guid id, CancellationToken ct)
    {
        if (!User.IsInRole(ColdstartRoles.SuperAdmin)) return Forbid();
        return await RunAsync(id, ct, cid => workflow.SendFromColdstartAsync(cid, id, ct));
    }
```

Add `using Coldstart.Infrastructure.Identity;` if `ColdstartRoles` is not already in scope (the file already imports that namespace for `ApplicationUser` — reuse it; use the literal `"SuperAdmin"` only if no constant is accessible).

- [ ] **Step 5: Add the button**

In `src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml`, inside the `@if (approved)` send panel, after the existing `ld-marksent` form (around line 81), add:

```html
          @if (User.IsInRole("SuperAdmin"))
          {
            <form method="post" class="ld-marksent">
              <button class="btn btn-primary" type="submit" asp-page-handler="SendFromColdstart" disabled="@(!hasRecipient)">Send from Coldstart ✓</button>
              <span class="ts">Sends the approved draft from your Coldstart address, branded.</span>
            </form>
          }
```

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~SendFromColdstart"` → PASS.
Then full `dotnet test` → green (report per-project totals).

- [ ] **Step 7: Commit**

```bash
git add src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml.cs src/Coldstart.Web/Areas/App/Pages/Leads/Detail.cshtml tests/Coldstart.Web.Tests/ColdstartWebFactory.cs tests/Coldstart.Web.Tests/LeadDetailTests.cs
git commit -m "feat(app): admin-only Send from Coldstart action on the lead detail"
```

---

## Post-implementation (manual, outside the plan)

- Live smoke: as the operator (SuperAdmin) approve a lead with your own address as recipient, click **Send from Coldstart**, confirm receipt + branded signature, and that the lead moves to Sent.
- Prod config: set `OutreachMailbox:*` env vars in the deploy stack (mirror the user-secrets values).

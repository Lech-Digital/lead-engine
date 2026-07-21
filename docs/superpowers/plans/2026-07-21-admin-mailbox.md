# Admin Mailbox Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A live-IMAP mini webmail for `info@getcoldstart.nl` inside the SuperAdmin area: inbox list, read (sanitized), reply/compose, attachments, mark-read/delete — per `docs/superpowers/specs/2026-07-21-admin-mailbox-design.md`.

**Architecture:** Hexagonal. `IMailbox` port + DTO records in Application; MailKit-based `ImapMailbox` adapter + `MailHtmlSanitizer` in Infrastructure (HTML sanitized at the boundary); Razor pages in `Areas/Admin/Pages/Mailbox/`. No mail stored in Postgres — IMAP is the source of truth. Resend keeps all transactional sending.

**Tech Stack:** .NET 10 Razor Pages, MailKit 4.17.0 (already referenced), `HtmlSanitizer` NuGet (new, latest 9.x), GreenMail Testcontainer for adapter integration tests, xUnit + FluentAssertions.

## Global Constraints

- Test style: xUnit + FluentAssertions, `MethodName_StateUnderTest_ExpectedBehavior`, `// Arrange` `// Act` `// Assert` comments. Full-solution `dotnet test` green at the end of every task (currently 152 tests; report real command + per-project totals).
- Commits: conventional style, NO Co-Authored-By trailers. Stage specific paths only — the repo has unrelated uncommitted user changes (docs, `_Footer.cshtml`, `Terms.cshtml`, `Privacy.cshtml`, `SubmitInquiry.cs`, `Harvester/appsettings.json`, `tests/test_email.py`) that must NEVER be staged.
- Mailbox credentials only via configuration (`Mailbox` section; user-secrets already hold `Mailbox:Host = web0173.zxcs.nl` (interim until LE cert covers mail.getcoldstart.nl), `Mailbox:Username`, `Mailbox:Password`). Never hardcode hosts/credentials outside `MailboxOptions` defaults.
- All `IMailbox` methods take `CancellationToken` as last parameter; adapters pass it into every MailKit call.
- Morning Engine identity for all UI: tokens `--daylight/--ink/--sunrise/--body-muted/--body-subtle/--line-soft`, display font via `var(--display)`, monospace (`.ts`) ONLY for timestamps.
- Admin pages: `[Authorize(Roles = ColdstartRoles.SuperAdmin)]` (copy the exact using from `Areas/Admin/Pages/Index.cshtml.cs`).
- Incoming HTML is sanitized in Infrastructure before crossing the port; Web renders `HtmlBody` as already-safe.
- CS-MOD-01: implementation classes `internal`; port + DTOs `public` (they cross layers, mirroring `LeadSummary`).

---

### Task 1: Port, DTOs, and HTML sanitizer

**Files:**
- Create: `src/Coldstart.Application/Mailbox/IMailbox.cs`
- Create: `src/Coldstart.Application/Mailbox/MailboxModels.cs`
- Create: `src/Coldstart.Infrastructure/Mailbox/MailHtmlSanitizer.cs`
- Modify: `src/Coldstart.Infrastructure/Coldstart.Infrastructure.csproj` (add HtmlSanitizer package)
- Test: `tests/Coldstart.Infrastructure.Tests/MailHtmlSanitizerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (later tasks depend on these EXACT shapes):

```csharp
// src/Coldstart.Application/Mailbox/IMailbox.cs
namespace Coldstart.Application.Mailbox;

public interface IMailbox
{
    Task<MailboxPage> ListAsync(int page, int pageSize, CancellationToken ct);
    Task<MailMessageDetail?> GetAsync(uint uid, CancellationToken ct);
    Task<MailAttachmentContent?> GetAttachmentAsync(uint uid, int index, CancellationToken ct);
    Task MarkReadAsync(uint uid, bool read, CancellationToken ct);
    Task DeleteAsync(uint uid, CancellationToken ct);
    Task SendAsync(OutgoingMail mail, CancellationToken ct);
}
```

```csharp
// src/Coldstart.Application/Mailbox/MailboxModels.cs
namespace Coldstart.Application.Mailbox;

public sealed record MailboxPage(IReadOnlyList<MailMessageSummary> Items, int TotalCount, int Page, int PageSize);

public sealed record MailMessageSummary(
    uint Uid, string FromName, string FromAddress, string Subject,
    DateTimeOffset Date, bool Seen, bool HasAttachments);

public sealed record MailMessageDetail(
    uint Uid, string FromName, string FromAddress, string To, string Subject,
    DateTimeOffset Date, bool Seen,
    string? HtmlBody,     // sanitized by the Infrastructure adapter — safe to render
    bool RemoteImagesRemoved,
    string TextBody,
    string? MessageId, string? References,
    IReadOnlyList<MailAttachmentInfo> Attachments);

public sealed record MailAttachmentInfo(int Index, string FileName, string ContentType, long Size);

public sealed record MailAttachmentContent(string FileName, string ContentType, byte[] Content);

public sealed record OutgoingMail(
    string To, string Subject, string TextBody,
    string? InReplyToMessageId = null, string? References = null);
```

- `MailHtmlSanitizer.Sanitize(string html)` returns `SanitizedHtml(string Html, bool RemoteImagesRemoved)`.

- [ ] **Step 1: Add the HtmlSanitizer package**

```bash
cd src/Coldstart.Infrastructure && dotnet add package HtmlSanitizer
```

Expected: resolves to 9.x. (Repo-root NuGet.Config pins nuget.org.)

- [ ] **Step 2: Write the failing sanitizer tests**

Create `tests/Coldstart.Infrastructure.Tests/MailHtmlSanitizerTests.cs`:

```csharp
using Coldstart.Infrastructure.Mailbox;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class MailHtmlSanitizerTests
{
    [Fact]
    public void Sanitize_WhenHtmlContainsScript_StripsIt()
    {
        // Arrange
        var html = "<p>hello</p><script>alert(1)</script>";

        // Act
        var result = MailHtmlSanitizer.Sanitize(html);

        // Assert
        result.Html.Should().Contain("hello");
        result.Html.Should().NotContain("script").And.NotContain("alert");
    }

    [Fact]
    public void Sanitize_WhenElementHasEventHandler_StripsTheHandler()
    {
        // Arrange
        var html = """<img src="cid:logo" onerror="alert(1)"><b onclick="x()">hi</b>""";

        // Act
        var result = MailHtmlSanitizer.Sanitize(html);

        // Assert
        result.Html.Should().NotContain("onerror").And.NotContain("onclick");
        result.Html.Should().Contain("hi");
    }

    [Fact]
    public void Sanitize_WhenImageIsRemote_RemovesItAndFlags()
    {
        // Arrange
        var html = """<p>text</p><img src="https://tracker.example/pixel.png">""";

        // Act
        var result = MailHtmlSanitizer.Sanitize(html);

        // Assert
        result.Html.Should().NotContain("tracker.example");
        result.RemoteImagesRemoved.Should().BeTrue();
    }

    [Fact]
    public void Sanitize_WhenImageIsInlineCid_KeepsIt()
    {
        // Arrange
        var html = """<img src="cid:part1.abc">""";

        // Act
        var result = MailHtmlSanitizer.Sanitize(html);

        // Assert
        result.Html.Should().Contain("cid:part1.abc");
        result.RemoteImagesRemoved.Should().BeFalse();
    }

    [Fact]
    public void Sanitize_WhenLinkIsJavascript_NeutralizesIt()
    {
        // Arrange
        var html = """<a href="javascript:alert(1)">click</a>""";

        // Act
        var result = MailHtmlSanitizer.Sanitize(html);

        // Assert
        result.Html.Should().NotContain("javascript:");
        result.Html.Should().Contain("click");
    }

    [Fact]
    public void Sanitize_WhenLinkIsHttps_ForcesNewTabAndNoopener()
    {
        // Arrange
        var html = """<a href="https://example.com">site</a>""";

        // Act
        var result = MailHtmlSanitizer.Sanitize(html);

        // Assert
        result.Html.Should().Contain("https://example.com");
        result.Html.Should().Contain("target=\"_blank\"");
        result.Html.Should().Contain("noopener");
    }

    [Fact]
    public void Sanitize_PlainFormatting_SurvivesUntouched()
    {
        // Arrange
        var html = "<p>Hi <strong>there</strong>,</p><ul><li>one</li></ul><table><tr><td>x</td></tr></table>";

        // Act
        var result = MailHtmlSanitizer.Sanitize(html);

        // Assert
        result.Html.Should().Contain("<strong>there</strong>");
        result.Html.Should().Contain("<li>one</li>");
        result.Html.Should().Contain("<td>x</td>");
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~MailHtmlSanitizer"`
Expected: compile error — `MailHtmlSanitizer` does not exist.

- [ ] **Step 4: Implement port, DTOs, sanitizer**

Create the two Application files exactly as in **Interfaces** above. Then create `src/Coldstart.Infrastructure/Mailbox/MailHtmlSanitizer.cs`:

```csharp
using Ganss.Xss;

namespace Coldstart.Infrastructure.Mailbox;

public sealed record SanitizedHtml(string Html, bool RemoteImagesRemoved);

// Boundary defense: mail HTML is hostile input. Everything the panel renders
// passes through here first; the Application layer only ever sees the output.
internal static class MailHtmlSanitizer
{
    public static SanitizedHtml Sanitize(string html)
    {
        var removedRemoteImage = false;
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedSchemes.Add("cid");          // inline images
        sanitizer.AllowedSchemes.Remove("data");

        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is not AngleSharp.Dom.IElement el) return;
            if (el.TagName.Equals("IMG", StringComparison.OrdinalIgnoreCase))
            {
                var src = el.GetAttribute("src") ?? "";
                if (!src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
                {
                    removedRemoteImage = true;
                    el.Remove();
                }
            }
            else if (el.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
            {
                el.SetAttribute("target", "_blank");
                el.SetAttribute("rel", "noopener noreferrer");
            }
        };

        var clean = sanitizer.Sanitize(html);
        return new SanitizedHtml(clean, removedRemoteImage);
    }
}
```

Note: `HtmlSanitizer` strips `<script>`, event handlers, and `javascript:` URLs by default — the tests pin that. If the package's namespace differs in 9.x (`Ganss.Xss` is correct for 8.x/9.x), check the package README and adjust the using.

`MailHtmlSanitizer` is consumed by `ImapMailbox` (Task 2) and its record `SanitizedHtml` is internal-to-Infrastructure territory but declared `public sealed record` in the same file for test access — tests live in `Coldstart.Infrastructure.Tests` which has no `InternalsVisibleTo`? Check: `grep -rn "InternalsVisibleTo" src/Coldstart.Infrastructure`. If present, make BOTH the class and record `internal`. If absent, keep the class `internal` only if tests still compile — otherwise follow the existing pattern used by `GroqLeadEnricher` tests (that class is `internal` and tested, so `InternalsVisibleTo` exists — mirror it).

- [ ] **Step 5: Run tests to verify pass**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~MailHtmlSanitizer"`
Expected: 7/7 PASS. Then full `dotnet test` → all green.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Application/Mailbox src/Coldstart.Infrastructure/Mailbox src/Coldstart.Infrastructure/Coldstart.Infrastructure.csproj tests/Coldstart.Infrastructure.Tests/MailHtmlSanitizerTests.cs
git commit -m "feat(mailbox): IMailbox port, DTOs, and hostile-HTML sanitizer"
```

---

### Task 2: ImapMailbox adapter + GreenMail integration tests

**Files:**
- Create: `src/Coldstart.Infrastructure/Mailbox/MailboxOptions.cs`
- Create: `src/Coldstart.Infrastructure/Mailbox/ImapMailbox.cs`
- Modify: `src/Coldstart.Infrastructure/DependencyInjection.cs` (register `IMailbox`)
- Modify: `src/Coldstart.Web/Program.cs` (bind `MailboxOptions` from `"Mailbox"` section, next to the existing `SmtpOptions` binding at line ~17)
- Test: `tests/Coldstart.Infrastructure.Tests/GreenMailFixture.cs`, `tests/Coldstart.Infrastructure.Tests/ImapMailboxTests.cs`

**Interfaces:**
- Consumes: Task 1 `IMailbox`, DTOs, `MailHtmlSanitizer`.
- Produces: `MailboxOptions { string Host = ""; int ImapPort = 993; int SmtpPort = 587; bool ImapUseSsl = true; bool SmtpUseStartTls = true; string Username = ""; string Password = ""; string FromName = "Coldstart"; }` (the two booleans exist so GreenMail tests can connect without TLS; production defaults stay secure). DI: `services.AddScoped<IMailbox, Mailbox.ImapMailbox>();`.

- [ ] **Step 1: Write the GreenMail fixture**

Create `tests/Coldstart.Infrastructure.Tests/GreenMailFixture.cs`:

```csharp
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

// Throwaway IMAP+SMTP server (greenmail). Plain-text ports: SMTP 3025, IMAP 3143.
// User is preconfigured via GREENMAIL_OPTS; login is the full email address.
public sealed class GreenMailFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("greenmail/standalone:2.1.3")
        .WithEnvironment("GREENMAIL_OPTS",
            "-Dgreenmail.setup.test.smtp -Dgreenmail.setup.test.imap " +
            "-Dgreenmail.hostname=0.0.0.0 " +
            "-Dgreenmail.users=info%40getcoldstart.test:secret " +
            "-Dgreenmail.users.login=email")
        .WithPortBinding(3025, true)
        .WithPortBinding(3143, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(3143))
        .Build();

    public string Host => _container.Hostname;
    public int SmtpPort => _container.GetMappedPublicPort(3025);
    public int ImapPort => _container.GetMappedPublicPort(3143);
    public const string User = "info@getcoldstart.test";
    public const string Password = "secret";

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
```

Note on `greenmail.users`: the `@` inside the user spec must be URL-encoded as `%40` (GreenMail convention for email-style logins: `login:pwd` where login contains `%40`). If authentication fails in Step 4, check the container log (`docker logs`) for the parsed user list and adjust — an alternative accepted format is `-Dgreenmail.users=info:secret@getcoldstart.test` (login `info`, domain appended); in that case set `User = "info"` here and keep `greenmail.users.login=email` removed. Pin whichever form the container actually accepts — the test assertions don't care about the login string beyond authentication succeeding.

- [ ] **Step 2: Write the failing integration tests**

Create `tests/Coldstart.Infrastructure.Tests/ImapMailboxTests.cs`:

```csharp
using Coldstart.Application.Mailbox;
using Coldstart.Infrastructure.Mailbox;
using FluentAssertions;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class ImapMailboxTests(GreenMailFixture greenMail) : IClassFixture<GreenMailFixture>
{
    private ImapMailbox Sut() => new(Options.Create(new MailboxOptions
    {
        Host = greenMail.Host,
        ImapPort = greenMail.ImapPort,
        SmtpPort = greenMail.SmtpPort,
        ImapUseSsl = false,
        SmtpUseStartTls = false,
        Username = GreenMailFixture.User,
        Password = GreenMailFixture.Password,
        FromName = "Coldstart",
    }));

    // Seeds one message into the INBOX by SMTP-delivering it to the test user.
    private async Task SeedAsync(string subject, string body, string? html = null)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("Prospect", "prospect@example.com"));
        msg.To.Add(MailboxAddress.Parse(GreenMailFixture.User));
        msg.Subject = subject;
        msg.Body = html is null
            ? new TextPart("plain") { Text = body }
            : new BodyBuilder { TextBody = body, HtmlBody = html }.ToMessageBody();
        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(greenMail.Host, greenMail.SmtpPort, MailKit.Security.SecureSocketOptions.None);
        await smtp.SendAsync(msg);
        await smtp.DisconnectAsync(true);
    }

    [Fact]
    public async Task ListAsync_AfterSeedingAMessage_ReturnsItNewestFirstAndUnseen()
    {
        // Arrange
        await SeedAsync("Hello Coldstart", "interested in leads");

        // Act
        var pageResult = await Sut().ListAsync(1, 25, CancellationToken.None);

        // Assert
        pageResult.TotalCount.Should().BeGreaterThan(0);
        var first = pageResult.Items[0];
        first.Subject.Should().Be("Hello Coldstart");
        first.FromAddress.Should().Be("prospect@example.com");
        first.Seen.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WithHtmlMessage_ReturnsSanitizedHtmlBody()
    {
        // Arrange
        await SeedAsync("Html mail", "plain fallback",
            html: """<p>hi</p><script>alert(1)</script><img src="https://x.example/p.png">""");
        var uid = (await Sut().ListAsync(1, 25, CancellationToken.None)).Items[0].Uid;

        // Act
        var detail = await Sut().GetAsync(uid, CancellationToken.None);

        // Assert
        detail.Should().NotBeNull();
        detail!.HtmlBody.Should().Contain("hi");
        detail.HtmlBody.Should().NotContain("script");
        detail.HtmlBody.Should().NotContain("x.example");
    }

    [Fact]
    public async Task MarkReadAsync_TogglesTheSeenFlag()
    {
        // Arrange
        await SeedAsync("Flag me", "body");
        var sut = Sut();
        var uid = (await sut.ListAsync(1, 25, CancellationToken.None)).Items[0].Uid;

        // Act
        await sut.MarkReadAsync(uid, read: true, CancellationToken.None);

        // Assert
        var afterRead = await sut.ListAsync(1, 25, CancellationToken.None);
        afterRead.Items.First(i => i.Uid == uid).Seen.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheMessageFromTheInbox()
    {
        // Arrange
        await SeedAsync("Delete me", "body");
        var sut = Sut();
        var uid = (await sut.ListAsync(1, 25, CancellationToken.None)).Items[0].Uid;

        // Act
        await sut.DeleteAsync(uid, CancellationToken.None);

        // Assert
        var after = await sut.ListAsync(1, 25, CancellationToken.None);
        after.Items.Should().NotContain(i => i.Uid == uid);
    }

    [Fact]
    public async Task SendAsync_DeliversWithThreadingHeaders()
    {
        // Arrange — send to ourselves so we can read it back over IMAP
        var mail = new OutgoingMail(GreenMailFixture.User, "Re: original",
            "reply text", InReplyToMessageId: "<orig-123@example.com>", References: "<orig-123@example.com>");
        var sut = Sut();

        // Act
        await sut.SendAsync(mail, CancellationToken.None);

        // Assert
        var list = await sut.ListAsync(1, 25, CancellationToken.None);
        var received = list.Items.First(i => i.Subject == "Re: original");
        var detail = await sut.GetAsync(received.Uid, CancellationToken.None);
        detail!.TextBody.Should().Contain("reply text");
        detail.References.Should().Contain("orig-123@example.com");
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~ImapMailbox"`
Expected: compile error — `MailboxOptions`/`ImapMailbox` missing.

- [ ] **Step 4: Implement `MailboxOptions` and `ImapMailbox`**

`src/Coldstart.Infrastructure/Mailbox/MailboxOptions.cs`:

```csharp
namespace Coldstart.Infrastructure.Mailbox;

public sealed class MailboxOptions
{
    public string Host { get; set; } = "";
    public int ImapPort { get; set; } = 993;
    public int SmtpPort { get; set; } = 587;
    public bool ImapUseSsl { get; set; } = true;       // false only for plaintext test servers
    public bool SmtpUseStartTls { get; set; } = true;  // false only for plaintext test servers
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromName { get; set; } = "Coldstart";
}
```

`src/Coldstart.Infrastructure/Mailbox/ImapMailbox.cs`:

```csharp
using Coldstart.Application.Mailbox;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Coldstart.Infrastructure.Mailbox;

internal sealed class ImapMailbox(IOptions<MailboxOptions> options) : IMailbox
{
    private readonly MailboxOptions _o = options.Value;

    private async Task<ImapClient> OpenAsync(CancellationToken ct)
    {
        var client = new ImapClient();
        // Same rationale as SmtpEmailSender: unreachable CRL endpoints must not kill the connection.
        client.CheckCertificateRevocation = false;
        await client.ConnectAsync(_o.Host, _o.ImapPort,
            _o.ImapUseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None, ct);
        await client.AuthenticateAsync(_o.Username, _o.Password, ct);
        return client;
    }

    public async Task<MailboxPage> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        using var client = await OpenAsync(ct);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

        var total = inbox.Count;
        // Newest first: highest sequence numbers are the most recent arrivals.
        var startIndex = Math.Max(0, total - page * pageSize);
        var count = Math.Min(pageSize, total - (page - 1) * pageSize);
        if (count <= 0)
            return new MailboxPage([], total, page, pageSize);

        var summaries = await inbox.FetchAsync(startIndex, startIndex + count - 1,
            MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope
            | MessageSummaryItems.Flags | MessageSummaryItems.BodyStructure, ct);

        var items = summaries
            .OrderByDescending(s => s.Index)
            .Select(s => new MailMessageSummary(
                s.UniqueId.Id,
                s.Envelope?.From?.Mailboxes.FirstOrDefault()?.Name ?? "",
                s.Envelope?.From?.Mailboxes.FirstOrDefault()?.Address ?? "",
                s.Envelope?.Subject ?? "(no subject)",
                s.Envelope?.Date ?? DateTimeOffset.MinValue,
                s.Flags?.HasFlag(MessageFlags.Seen) ?? false,
                s.Attachments.Any()))
            .ToList();

        await client.DisconnectAsync(true, ct);
        return new MailboxPage(items, total, page, pageSize);
    }

    public async Task<MailMessageDetail?> GetAsync(uint uid, CancellationToken ct)
    {
        using var client = await OpenAsync(ct);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

        var id = new UniqueId(uid);
        MimeMessage message;
        try { message = await inbox.GetMessageAsync(id, ct); }
        catch (MessageNotFoundException) { return null; }

        var summary = (await inbox.FetchAsync([id],
            MessageSummaryItems.Flags | MessageSummaryItems.UniqueId, ct)).FirstOrDefault();

        string? html = null;
        var remoteImagesRemoved = false;
        if (!string.IsNullOrEmpty(message.HtmlBody))
        {
            var sanitized = MailHtmlSanitizer.Sanitize(message.HtmlBody);
            html = sanitized.Html;
            remoteImagesRemoved = sanitized.RemoteImagesRemoved;
        }

        var attachments = message.Attachments
            .Select((a, i) => new MailAttachmentInfo(
                i,
                a.ContentDisposition?.FileName ?? a.ContentType.Name ?? $"attachment-{i}",
                a.ContentType.MimeType,
                (a as MimePart)?.Content?.Stream?.Length ?? 0))
            .ToList();

        var from = message.From.Mailboxes.FirstOrDefault();
        var detail = new MailMessageDetail(
            uid,
            from?.Name ?? "",
            from?.Address ?? "",
            string.Join(", ", message.To.Mailboxes.Select(m => m.Address)),
            message.Subject ?? "(no subject)",
            message.Date,
            summary?.Flags?.HasFlag(MessageFlags.Seen) ?? false,
            html,
            remoteImagesRemoved,
            message.TextBody ?? "",
            message.MessageId is null ? null : $"<{message.MessageId}>",
            message.References.Count == 0 ? null : string.Join(" ", message.References.Select(r => $"<{r}>")),
            attachments);

        await client.DisconnectAsync(true, ct);
        return detail;
    }

    public async Task<MailAttachmentContent?> GetAttachmentAsync(uint uid, int index, CancellationToken ct)
    {
        using var client = await OpenAsync(ct);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

        MimeMessage message;
        try { message = await inbox.GetMessageAsync(new UniqueId(uid), ct); }
        catch (MessageNotFoundException) { return null; }

        var attachment = message.Attachments.ElementAtOrDefault(index);
        if (attachment is not MimePart part) return null;

        using var ms = new MemoryStream();
        await part.Content.DecodeToAsync(ms, ct);
        await client.DisconnectAsync(true, ct);
        return new MailAttachmentContent(
            part.ContentDisposition?.FileName ?? part.ContentType.Name ?? $"attachment-{index}",
            part.ContentType.MimeType,
            ms.ToArray());
    }

    public async Task MarkReadAsync(uint uid, bool read, CancellationToken ct)
    {
        using var client = await OpenAsync(ct);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite, ct);
        if (read) await inbox.AddFlagsAsync([new UniqueId(uid)], MessageFlags.Seen, silent: true, ct);
        else await inbox.RemoveFlagsAsync([new UniqueId(uid)], MessageFlags.Seen, silent: true, ct);
        await client.DisconnectAsync(true, ct);
    }

    public async Task DeleteAsync(uint uid, CancellationToken ct)
    {
        using var client = await OpenAsync(ct);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite, ct);

        var trash = client.PersonalNamespaces.Count > 0
            ? await FindTrashAsync(client, ct)
            : null;

        var id = new UniqueId(uid);
        if (trash is not null)
        {
            await inbox.MoveToAsync(id, trash, ct);
        }
        else
        {
            await inbox.AddFlagsAsync([id], MessageFlags.Deleted, silent: true, ct);
            await inbox.ExpungeAsync(ct);
        }
        await client.DisconnectAsync(true, ct);
    }

    private static async Task<IMailFolder?> FindTrashAsync(ImapClient client, CancellationToken ct)
    {
        try
        {
            var trash = client.GetFolder(SpecialFolder.Trash);
            if (trash is not null) return trash;
        }
        catch (NotSupportedException) { /* server lacks SPECIAL-USE; fall through to name lookup */ }
        var personal = client.GetFolder(client.PersonalNamespaces[0]);
        var folders = await personal.GetSubfoldersAsync(false, ct);
        return folders.FirstOrDefault(f => f.Name.Equals("Trash", StringComparison.OrdinalIgnoreCase));
    }

    public async Task SendAsync(OutgoingMail mail, CancellationToken ct)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_o.FromName, _o.Username));
        msg.To.Add(MailboxAddress.Parse(mail.To));
        msg.Subject = mail.Subject;
        msg.Body = new TextPart("plain") { Text = mail.TextBody };
        if (!string.IsNullOrEmpty(mail.InReplyToMessageId))
            msg.InReplyTo = mail.InReplyToMessageId.Trim('<', '>');
        if (!string.IsNullOrEmpty(mail.References))
            foreach (var r in mail.References.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                msg.References.Add(r.Trim('<', '>'));

        using var client = new MailKit.Net.Smtp.SmtpClient();
        client.CheckCertificateRevocation = false;
        await client.ConnectAsync(_o.Host, _o.SmtpPort,
            _o.SmtpUseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
        await client.AuthenticateAsync(_o.Username, _o.Password, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }
}
```

Implementation notes for the engineer:
- MailKit API details may differ slightly (e.g. `FetchAsync` overload shapes, `IFetchRequest` in newer versions). If a call doesn't compile, check the MailKit 4.17 API — the INTENT of each block is authoritative, method-name drift is expected and fine.
- GreenMail may not support `MoveTo`/Trash — `FindTrashAsync` returning null and the expunge path covering it is the expected test behavior.

- [ ] **Step 5: Wire DI**

In `src/Coldstart.Infrastructure/DependencyInjection.cs`, after the `IEmailSender` registration (line ~17):

```csharp
services.AddScoped<Application.Mailbox.IMailbox, Mailbox.ImapMailbox>();
```

In `src/Coldstart.Web/Program.cs`, next to the `SmtpOptions` binding:

```csharp
builder.Services.Configure<Coldstart.Infrastructure.Mailbox.MailboxOptions>(builder.Configuration.GetSection("Mailbox"));
```

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/Coldstart.Infrastructure.Tests --filter "FullyQualifiedName~ImapMailbox|FullyQualifiedName~MailHtmlSanitizer"`
Expected: all PASS (GreenMail container pulls on first run — allow a minute).
Then full `dotnet test` → green.

- [ ] **Step 7: Commit**

```bash
git add src/Coldstart.Infrastructure/Mailbox src/Coldstart.Infrastructure/DependencyInjection.cs src/Coldstart.Web/Program.cs tests/Coldstart.Infrastructure.Tests/GreenMailFixture.cs tests/Coldstart.Infrastructure.Tests/ImapMailboxTests.cs
git commit -m "feat(mailbox): MailKit ImapMailbox adapter with GreenMail integration tests"
```

---

### Task 3: Inbox page + FakeMailbox + admin nav

**Files:**
- Create: `src/Coldstart.Web/Areas/Admin/Pages/Mailbox/Index.cshtml` + `Index.cshtml.cs`
- Modify: `src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml` (nav link)
- Modify: `src/Coldstart.Web/wwwroot/css/site.css` (mailbox CSS layer, appended)
- Create: `tests/Coldstart.Web.Tests/FakeMailbox.cs`
- Modify: `tests/Coldstart.Web.Tests/ColdstartWebFactory.cs` (register fake)
- Test: `tests/Coldstart.Web.Tests/MailboxPagesTests.cs`

**Interfaces:**
- Consumes: `IMailbox` + DTOs (Task 1).
- Produces: `FakeMailbox : IMailbox` with `public List<MailMessageDetail> Messages { get; }`, `public List<OutgoingMail> Sent { get; }`, `public bool Unreachable { get; set; }` (when true every method throws `System.Net.Sockets.SocketException`); factory property `public FakeMailbox Mailbox { get; } = new();` on `ColdstartWebFactory`. Route `/admin/mailbox`. CSS classes `.mail-list`, `.mail-row`, `.mail-row-unseen`, `.mail-error` used by later tasks' tests.

- [ ] **Step 1: Write FakeMailbox**

Create `tests/Coldstart.Web.Tests/FakeMailbox.cs`:

```csharp
using Coldstart.Application.Mailbox;

namespace Coldstart.Web.Tests;

// In-memory IMailbox: Messages is the INBOX (newest = highest Uid), Sent captures outgoing mail.
public sealed class FakeMailbox : IMailbox
{
    public List<MailMessageDetail> Messages { get; } = [];
    public List<OutgoingMail> Sent { get; } = [];
    public bool Unreachable { get; set; }

    private void ThrowIfUnreachable()
    {
        if (Unreachable) throw new System.Net.Sockets.SocketException();
    }

    public Task<MailboxPage> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        ThrowIfUnreachable();
        var ordered = Messages.OrderByDescending(m => m.Uid).ToList();
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new MailMessageSummary(m.Uid, m.FromName, m.FromAddress, m.Subject, m.Date, m.Seen, m.Attachments.Count > 0))
            .ToList();
        return Task.FromResult(new MailboxPage(items, Messages.Count, page, pageSize));
    }

    public Task<MailMessageDetail?> GetAsync(uint uid, CancellationToken ct)
    {
        ThrowIfUnreachable();
        return Task.FromResult(Messages.FirstOrDefault(m => m.Uid == uid));
    }

    public Task<MailAttachmentContent?> GetAttachmentAsync(uint uid, int index, CancellationToken ct)
    {
        ThrowIfUnreachable();
        var msg = Messages.FirstOrDefault(m => m.Uid == uid);
        var info = msg?.Attachments.ElementAtOrDefault(index);
        return Task.FromResult(info is null
            ? null
            : new MailAttachmentContent(info.FileName, info.ContentType, [1, 2, 3]));
    }

    public Task MarkReadAsync(uint uid, bool read, CancellationToken ct)
    {
        ThrowIfUnreachable();
        var i = Messages.FindIndex(m => m.Uid == uid);
        if (i >= 0) Messages[i] = Messages[i] with { Seen = read };
        return Task.CompletedTask;
    }

    public Task DeleteAsync(uint uid, CancellationToken ct)
    {
        ThrowIfUnreachable();
        Messages.RemoveAll(m => m.Uid == uid);
        return Task.CompletedTask;
    }

    public Task SendAsync(OutgoingMail mail, CancellationToken ct)
    {
        ThrowIfUnreachable();
        Sent.Add(mail);
        return Task.CompletedTask;
    }
}
```

In `ColdstartWebFactory.cs` add the property and registration (mirroring `Email`):

```csharp
public FakeMailbox Mailbox { get; } = new();
// inside ConfigureServices:
services.RemoveAll<Coldstart.Application.Mailbox.IMailbox>();
services.AddSingleton<Coldstart.Application.Mailbox.IMailbox>(Mailbox);
```

- [ ] **Step 2: Write the failing tests**

Create `tests/Coldstart.Web.Tests/MailboxPagesTests.cs`. IMPORTANT: read `tests/Coldstart.Web.Tests/AdminConsoleTests.cs` first and copy its exact pattern for creating + logging in a SuperAdmin client (helper in `TestAuth`); use that in every test below. Seed helper for this file:

```csharp
private static MailMessageDetail Msg(uint uid, string subject, bool seen = false, string? html = null,
    IReadOnlyList<MailAttachmentInfo>? atts = null) =>
    new(uid, "Prospect", "prospect@example.com", "info@getcoldstart.nl", subject,
        DateTimeOffset.UtcNow, seen, html, false, "plain body", $"<msg-{uid}@example.com>", null, atts ?? []);
```

Tests (AAA + FluentAssertions):

```csharp
[Fact]
public async Task Inbox_ListsMessages_NewestFirstWithUnseenStyling()
{
    // Arrange — SuperAdmin client per AdminConsoleTests pattern
    factory.Mailbox.Messages.Clear();
    factory.Mailbox.Messages.Add(Msg(1, "Older", seen: true));
    factory.Mailbox.Messages.Add(Msg(2, "Newer"));

    // Act
    var html = await client.GetStringAsync("/admin/mailbox");

    // Assert
    html.Should().Contain("Newer").And.Contain("Older");
    html.IndexOf("Newer").Should().BeLessThan(html.IndexOf("Older"));
    html.Should().Contain("mail-row-unseen");
}

[Fact]
public async Task Inbox_WhenMailboxUnreachable_ShowsFriendlyErrorNot500()
{
    // Arrange
    factory.Mailbox.Unreachable = true;
    try
    {
        // Act
        var response = await client.GetAsync("/admin/mailbox");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Mailbox unreachable");
    }
    finally { factory.Mailbox.Unreachable = false; }
}

[Fact]
public async Task Inbox_WhenNotSuperAdmin_IsDenied()
{
    // Arrange — anonymous client (no login)
    var anon = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Act
    var response = await anon.GetAsync("/admin/mailbox");

    // Assert — Identity redirects unauthenticated users to login
    ((int)response.StatusCode).Should().BeOneOf(302, 401, 403);
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/Coldstart.Web.Tests --filter "FullyQualifiedName~MailboxPages"`
Expected: FAIL — 404 on `/admin/mailbox` (page doesn't exist).

- [ ] **Step 4: Implement the Inbox page**

`src/Coldstart.Web/Areas/Admin/Pages/Mailbox/Index.cshtml.cs`:

```csharp
using Coldstart.Application.Mailbox;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.Admin.Pages.Mailbox;

[Authorize(Roles = ColdstartRoles.SuperAdmin)]
public sealed class IndexModel(IMailbox mailbox, ILogger<IndexModel> logger) : PageModel
{
    public MailboxPage? Mail { get; private set; }
    public bool Unreachable { get; private set; }
    public int Page_ { get; private set; } = 1;

    public async Task OnGetAsync(int page = 1, CancellationToken ct = default)
    {
        Page_ = Math.Max(1, page);
        try
        {
            Mail = await mailbox.ListAsync(Page_, 25, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Mailbox unreachable while listing inbox.");
            Unreachable = true;
        }
    }
}
```

`src/Coldstart.Web/Areas/Admin/Pages/Mailbox/Index.cshtml`:

```html
@page "/admin/mailbox"
@model Coldstart.Web.Areas.Admin.Pages.Mailbox.IndexModel
@{ ViewData["Title"] = "Mailbox — Coldstart admin"; Layout = "/Pages/Shared/_Layout.cshtml"; }

<header class="app-bar">
  <a class="wordmark" href="/">coldstart<i>*</i></a>
  <nav class="app-nav">
    <a href="/admin">Admin</a>
    <a href="/admin/mailbox" aria-current="page">Mailbox</a>
    <form method="post" asp-page="/Account/Logout" asp-area="Identity"><button class="btn btn-ghost" type="submit">Log out</button></form>
  </nav>
</header>

<main class="wrap app-zone">
  <section class="app-briefing">
    <h1>Mailbox</h1>
    <p class="app-briefing-ts">info@getcoldstart.nl · INBOX</p>
  </section>

  <div class="mail-actions">
    <a class="btn btn-primary" href="/admin/mailbox/compose">Compose</a>
    <a class="btn btn-ghost" href="/admin/mailbox">Refresh</a>
  </div>

  @if (Model.Unreachable)
  {
    <p class="mail-error">Mailbox unreachable right now — check the connection and try again.</p>
  }
  else if (Model.Mail is { } mail)
  {
    @if (@TempData["MailFlash"] is string flash) { <p class="mail-flash">@flash</p> }
    <div class="mail-list">
      @foreach (var m in mail.Items)
      {
        <a class="mail-row @(m.Seen ? "" : "mail-row-unseen")" href="/admin/mailbox/@m.Uid">
          <span class="mail-from">@(string.IsNullOrWhiteSpace(m.FromName) ? m.FromAddress : m.FromName)</span>
          <span class="mail-subj">@m.Subject @(m.HasAttachments ? "📎" : "")</span>
          <span class="ts mail-date">@m.Date.ToString("dd MMM · HH:mm", System.Globalization.CultureInfo.InvariantCulture)</span>
        </a>
      }
      @if (mail.Items.Count == 0) { <p class="mail-empty">Inbox empty — nothing to read.</p> }
    </div>
    @if (mail.TotalCount > mail.Page * mail.PageSize)
    {
      <a class="btn btn-ghost" href="/admin/mailbox?page=@(mail.Page + 1)">Older →</a>
    }
    @if (mail.Page > 1)
    {
      <a class="btn btn-ghost" href="/admin/mailbox?page=@(mail.Page - 1)">← Newer</a>
    }
  }
</main>
```

Add to `Areas/Admin/Pages/Index.cshtml`'s `app-nav` (before the logout form): `<a href="/admin/mailbox">Mailbox</a>`.

Append the mailbox CSS layer to `site.css`:

```css
/* ===== Admin mailbox ===== */
.mail-actions { display: flex; gap: 10px; margin: 14px 0 18px; }
.mail-list { display: flex; flex-direction: column; border: 1px solid var(--line-soft); border-radius: 12px; background: #fff; overflow: hidden; }
.mail-row { display: grid; grid-template-columns: 200px 1fr auto; gap: 14px; padding: 13px 18px; text-decoration: none; color: var(--body-muted); border-bottom: 1px solid var(--line-soft); align-items: baseline; }
.mail-row:last-child { border-bottom: none; }
.mail-row:hover { background: var(--daylight); }
.mail-row-unseen .mail-from, .mail-row-unseen .mail-subj { font-weight: 700; color: var(--ink); }
.mail-from { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.mail-subj { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.mail-date { font-size: 0.78rem; color: var(--body-subtle); }
.mail-empty, .mail-error, .mail-flash, .mail-note { padding: 16px 2px; color: var(--body-subtle); }
.mail-note { font-size: 0.85rem; padding: 8px 2px; }
.mail-error { color: #B3402A; }
.mail-flash { color: #2E7D4F; }
.mail-html { contain: content; max-width: 100%; overflow-x: auto; background: #fff; border: 1px solid var(--line-soft); border-radius: 10px; padding: 18px 20px; }
.mail-html img { max-width: 100%; }
.mail-text { white-space: pre-wrap; font-family: var(--body); background: #fff; border: 1px solid var(--line-soft); border-radius: 10px; padding: 18px 20px; color: var(--body-muted); }
.mail-head { margin: 6px 0 14px; }
.mail-head dt { font-size: 0.78rem; color: var(--body-subtle); }
.mail-head dd { margin: 0 0 6px; color: var(--ink); }
.mail-attachments { margin-top: 14px; display: flex; flex-direction: column; gap: 6px; }
@media (max-width: 640px) { .mail-row { grid-template-columns: 1fr auto; } .mail-from { display: none; } }
/* ===== end admin mailbox ===== */
```

- [ ] **Step 5: Run tests** — filter `MailboxPages` → 3/3 PASS; then full `dotnet test` → green.

- [ ] **Step 6: Commit**

```bash
git add src/Coldstart.Web/Areas/Admin/Pages/Mailbox src/Coldstart.Web/Areas/Admin/Pages/Index.cshtml src/Coldstart.Web/wwwroot/css/site.css tests/Coldstart.Web.Tests/FakeMailbox.cs tests/Coldstart.Web.Tests/ColdstartWebFactory.cs tests/Coldstart.Web.Tests/MailboxPagesTests.cs
git commit -m "feat(mailbox): admin inbox page with fake-backed web tests"
```

---

### Task 4: Message page — read, flags, delete, attachments

**Files:**
- Create: `src/Coldstart.Web/Areas/Admin/Pages/Mailbox/Message.cshtml` + `Message.cshtml.cs`
- Test: append to `tests/Coldstart.Web.Tests/MailboxPagesTests.cs`

**Interfaces:**
- Consumes: `IMailbox`, `FakeMailbox`, CSS classes from Task 3.
- Produces: routes `/admin/mailbox/{uid}` (GET marks read; POST handlers `MarkUnread`, `Delete`) and `/admin/mailbox/{uid}?handler=Attachment&index=N` for downloads. Compose (Task 5) links to `/admin/mailbox/compose?replyTo={uid}`.

- [ ] **Step 1: Write the failing tests** (same SuperAdmin client pattern; `Msg` helper from Task 3):

```csharp
[Fact]
public async Task Message_OnOpen_RendersSanitizedHtmlAndMarksRead()
{
    // Arrange
    factory.Mailbox.Messages.Clear();
    factory.Mailbox.Messages.Add(Msg(7, "Read me", html: "<p>safe <strong>html</strong></p>"));

    // Act
    var html = await client.GetStringAsync("/admin/mailbox/7");

    // Assert
    html.Should().Contain("safe <strong>html</strong>");
    factory.Mailbox.Messages.Single(m => m.Uid == 7).Seen.Should().BeTrue();
}

[Fact]
public async Task Message_MarkUnread_ClearsSeenAndRedirects()
{
    // Arrange
    factory.Mailbox.Messages.Clear();
    factory.Mailbox.Messages.Add(Msg(8, "Unread me", seen: true));
    var token = await ScrapeAntiforgeryAsync(client, "/admin/mailbox/8");   // mirror the file's existing token helper or AdminConsoleTests'

    // Act
    var response = await client.PostAsync("/admin/mailbox/8?handler=MarkUnread",
        new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    factory.Mailbox.Messages.Single(m => m.Uid == 8).Seen.Should().BeFalse();
}

[Fact]
public async Task Message_Delete_RemovesAndRedirectsToInbox()
{
    // Arrange
    factory.Mailbox.Messages.Clear();
    factory.Mailbox.Messages.Add(Msg(9, "Delete me"));
    var token = await ScrapeAntiforgeryAsync(client, "/admin/mailbox/9");

    // Act
    var response = await client.PostAsync("/admin/mailbox/9?handler=Delete",
        new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    response.Headers.Location!.ToString().Should().Contain("/admin/mailbox");
    factory.Mailbox.Messages.Should().NotContain(m => m.Uid == 9);
}

[Fact]
public async Task Message_AttachmentDownload_StreamsBytes_And404sOutOfRange()
{
    // Arrange
    factory.Mailbox.Messages.Clear();
    factory.Mailbox.Messages.Add(Msg(10, "With attachment",
        atts: [new MailAttachmentInfo(0, "report.pdf", "application/pdf", 3)]));

    // Act
    var ok = await client.GetAsync("/admin/mailbox/10?handler=Attachment&index=0");
    var missing = await client.GetAsync("/admin/mailbox/10?handler=Attachment&index=5");

    // Assert
    ok.StatusCode.Should().Be(HttpStatusCode.OK);
    ok.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
    ok.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
    (await ok.Content.ReadAsByteArrayAsync()).Should().Equal(1, 2, 3);
    missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
}

[Fact]
public async Task Message_UnknownUid_RedirectsToInbox()
{
    // Arrange
    factory.Mailbox.Messages.Clear();

    // Act
    var response = await client.GetAsync("/admin/mailbox/999");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    response.Headers.Location!.ToString().Should().Contain("/admin/mailbox");
}
```

(If `AllowAutoRedirect` is enabled on the shared client, create a no-redirect client for the redirect assertions — mirror how existing tests assert redirects.)

- [ ] **Step 2: Run to verify failure** — 404s / missing page.

- [ ] **Step 3: Implement**

`Message.cshtml.cs`:

```csharp
using Coldstart.Application.Mailbox;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.Admin.Pages.Mailbox;

[Authorize(Roles = ColdstartRoles.SuperAdmin)]
public sealed class MessageModel(IMailbox mailbox, ILogger<MessageModel> logger) : PageModel
{
    public MailMessageDetail? Mail { get; private set; }
    public bool Unreachable { get; private set; }

    public async Task<IActionResult> OnGetAsync(uint uid, CancellationToken ct)
    {
        try
        {
            Mail = await mailbox.GetAsync(uid, ct);
            if (Mail is null) return RedirectToPage("/Mailbox/Index", new { area = "Admin" });
            if (!Mail.Seen) await mailbox.MarkReadAsync(uid, read: true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Mailbox unreachable while opening message {Uid}.", uid);
            Unreachable = true;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostMarkUnreadAsync(uint uid, CancellationToken ct)
    {
        await mailbox.MarkReadAsync(uid, read: false, ct);
        return RedirectToPage("/Mailbox/Index", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(uint uid, CancellationToken ct)
    {
        await mailbox.DeleteAsync(uid, ct);
        TempData["MailFlash"] = "Message deleted.";
        return RedirectToPage("/Mailbox/Index", new { area = "Admin" });
    }

    public async Task<IActionResult> OnGetAttachmentAsync(uint uid, int index, CancellationToken ct)
    {
        var att = await mailbox.GetAttachmentAsync(uid, index, ct);
        if (att is null) return NotFound();
        return File(att.Content, att.ContentType, att.FileName);   // always Content-Disposition: attachment
    }
}
```

`Message.cshtml`:

```html
@page "/admin/mailbox/{uid}"
@model Coldstart.Web.Areas.Admin.Pages.Mailbox.MessageModel
@{ ViewData["Title"] = "Message — Coldstart admin"; Layout = "/Pages/Shared/_Layout.cshtml"; var m = Model.Mail; }

<header class="app-bar">
  <a class="wordmark" href="/">coldstart<i>*</i></a>
  <nav class="app-nav"><a href="/admin">Admin</a><a href="/admin/mailbox">Mailbox</a></nav>
</header>

<main class="wrap app-zone">
  <p class="auth-alt"><a href="/admin/mailbox">← Back to inbox</a></p>

  @if (Model.Unreachable)
  {
    <p class="mail-error">Mailbox unreachable right now — check the connection and try again.</p>
  }
  else if (m is not null)
  {
    <section class="app-briefing"><h1>@m.Subject</h1></section>
    <dl class="mail-head">
      <dt>From</dt><dd>@(string.IsNullOrWhiteSpace(m.FromName) ? m.FromAddress : $"{m.FromName} <{m.FromAddress}>")</dd>
      <dt>To</dt><dd>@m.To</dd>
      <dt>Date</dt><dd><span class="ts">@m.Date.ToString("dd MMM yyyy · HH:mm", System.Globalization.CultureInfo.InvariantCulture)</span></dd>
    </dl>

    <div class="mail-actions">
      <a class="btn btn-primary" href="/admin/mailbox/compose?replyTo=@m.Uid">Reply</a>
      <form method="post" asp-page-handler="MarkUnread" asp-route-uid="@m.Uid"><button class="btn btn-ghost" type="submit">Mark unread</button></form>
      <button class="btn btn-ghost" type="button" onclick="document.getElementById('confirm-delete').showModal()">Delete</button>
    </div>

    @if (m.HtmlBody is not null)
    {
      @if (m.RemoteImagesRemoved) { <p class="mail-note">Remote images were removed for your safety.</p> }
      <div class="mail-html">@Html.Raw(m.HtmlBody)</div>   @* sanitized in Infrastructure — see spec *@
    }
    else
    {
      <div class="mail-text">@m.TextBody</div>
    }

    @if (m.Attachments.Count > 0)
    {
      <div class="mail-attachments">
        @foreach (var a in m.Attachments)
        {
          <a class="app-link-out" href="/admin/mailbox/@m.Uid?handler=Attachment&index=@a.Index">📎 @a.FileName (@a.ContentType)</a>
        }
      </div>
    }

    <dialog id="confirm-delete" class="lead-modal">
      <form method="post" asp-page-handler="Delete" asp-route-uid="@m.Uid" class="lead-modal-card">
        <p class="lead-modal-eyebrow">Delete message</p>
        <h2 class="lead-modal-title">Delete "@m.Subject"?</h2>
        <div class="lead-modal-actions">
          <button class="btn btn-primary" type="submit">Delete</button>
          <button class="btn btn-ghost" type="button" onclick="this.closest('dialog').close()">Cancel</button>
        </div>
      </form>
    </dialog>
  }
</main>
```

- [ ] **Step 4: Run tests** — filter `MailboxPages` → all PASS; full `dotnet test` → green.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Web/Areas/Admin/Pages/Mailbox tests/Coldstart.Web.Tests/MailboxPagesTests.cs
git commit -m "feat(mailbox): message view with read flags, delete, and attachment download"
```

---

### Task 5: Compose and reply

**Files:**
- Create: `src/Coldstart.Web/Areas/Admin/Pages/Mailbox/Compose.cshtml` + `Compose.cshtml.cs`
- Test: append to `tests/Coldstart.Web.Tests/MailboxPagesTests.cs`

**Interfaces:**
- Consumes: `IMailbox.SendAsync(OutgoingMail)`, `IMailbox.GetAsync` (reply prefill), `FakeMailbox.Sent`.
- Produces: route `/admin/mailbox/compose` (GET optionally `?replyTo=uid`, POST sends).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Compose_Post_SendsMailAndRedirectsWithFlash()
{
    // Arrange
    factory.Mailbox.Sent.Clear();
    var token = await ScrapeAntiforgeryAsync(client, "/admin/mailbox/compose");

    // Act
    var response = await client.PostAsync("/admin/mailbox/compose", new FormUrlEncodedContent(
        new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.To"] = "prospect@example.com",
            ["Input.Subject"] = "Invitation to Coldstart",
            ["Input.Body"] = "Hi — here is your invite.",
        }));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    factory.Mailbox.Sent.Should().ContainSingle(s =>
        s.To == "prospect@example.com" && s.Subject == "Invitation to Coldstart");
}

[Fact]
public async Task Compose_AsReply_PrefillsQuoteAndThreadsCorrectly()
{
    // Arrange
    factory.Mailbox.Messages.Clear();
    factory.Mailbox.Sent.Clear();
    factory.Mailbox.Messages.Add(Msg(21, "Question about pricing"));

    // Act — GET prefill, then POST the reply
    var form = await client.GetStringAsync("/admin/mailbox/compose?replyTo=21");
    var token = await ScrapeAntiforgeryAsync(client, "/admin/mailbox/compose?replyTo=21");
    await client.PostAsync("/admin/mailbox/compose", new FormUrlEncodedContent(
        new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.To"] = "prospect@example.com",
            ["Input.Subject"] = "Re: Question about pricing",
            ["Input.Body"] = "Answer.",
            ["Input.InReplyTo"] = "<msg-21@example.com>",
            ["Input.References"] = "<msg-21@example.com>",
        }));

    // Assert
    form.Should().Contain("Re: Question about pricing");
    form.Should().Contain("wrote:");                       // quoted original marker
    factory.Mailbox.Sent.Should().ContainSingle(s => s.InReplyToMessageId == "<msg-21@example.com>");
}

[Fact]
public async Task Compose_InvalidEmail_ShowsErrorAndSendsNothing()
{
    // Arrange
    factory.Mailbox.Sent.Clear();
    var token = await ScrapeAntiforgeryAsync(client, "/admin/mailbox/compose");

    // Act
    var response = await client.PostAsync("/admin/mailbox/compose", new FormUrlEncodedContent(
        new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.To"] = "not-an-email",
            ["Input.Subject"] = "x",
            ["Input.Body"] = "y",
        }));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);   // page re-rendered with validation error
    factory.Mailbox.Sent.Should().BeEmpty();
}
```

- [ ] **Step 2: Run to verify failure** — 404 on compose.

- [ ] **Step 3: Implement**

`Compose.cshtml.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Coldstart.Application.Mailbox;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.Admin.Pages.Mailbox;

[Authorize(Roles = ColdstartRoles.SuperAdmin)]
public sealed class ComposeModel(IMailbox mailbox, ILogger<ComposeModel> logger) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public string? SendError { get; private set; }

    public async Task OnGetAsync(uint? replyTo, CancellationToken ct)
    {
        if (replyTo is null) return;
        try
        {
            var original = await mailbox.GetAsync(replyTo.Value, ct);
            if (original is null) return;
            Input = new InputModel
            {
                To = original.FromAddress,
                Subject = original.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
                    ? original.Subject : $"Re: {original.Subject}",
                Body = "\n\nOn " + original.Date.ToString("dd MMM yyyy HH:mm",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    $", {(string.IsNullOrWhiteSpace(original.FromName) ? original.FromAddress : original.FromName)} wrote:\n" +
                    string.Join("\n", original.TextBody.Split('\n').Select(l => "> " + l)),
                InReplyTo = original.MessageId,
                References = string.IsNullOrEmpty(original.References)
                    ? original.MessageId
                    : $"{original.References} {original.MessageId}",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Mailbox unreachable while preparing reply to {Uid}.", replyTo);
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return Page();
        try
        {
            await mailbox.SendAsync(new OutgoingMail(
                Input.To, Input.Subject, Input.Body, Input.InReplyTo, Input.References), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Sending mail from the admin mailbox failed.");
            SendError = "Sending failed — the mail server didn't accept the message. Your text is preserved; try again.";
            return Page();
        }
        TempData["MailFlash"] = "Sent.";
        return RedirectToPage("/Mailbox/Index", new { area = "Admin" });
    }

    public sealed class InputModel
    {
        [Required, EmailAddress] public string To { get; set; } = "";
        [Required] public string Subject { get; set; } = "";
        [Required] public string Body { get; set; } = "";
        public string? InReplyTo { get; set; }
        public string? References { get; set; }
    }
}
```

`Compose.cshtml`:

```html
@page "/admin/mailbox/compose"
@model Coldstart.Web.Areas.Admin.Pages.Mailbox.ComposeModel
@{ ViewData["Title"] = "Compose — Coldstart admin"; Layout = "/Pages/Shared/_Layout.cshtml"; }

<header class="app-bar">
  <a class="wordmark" href="/">coldstart<i>*</i></a>
  <nav class="app-nav"><a href="/admin">Admin</a><a href="/admin/mailbox">Mailbox</a></nav>
</header>

<main class="wrap app-zone">
  <p class="auth-alt"><a href="/admin/mailbox">← Back to inbox</a></p>
  <section class="app-briefing"><h1>New message</h1><p class="app-briefing-ts">from info@getcoldstart.nl</p></section>

  @if (Model.SendError is not null) { <p class="mail-error">@Model.SendError</p> }
  <div asp-validation-summary="All" class="field-error"></div>

  <form method="post" class="lp-form">
    <input type="hidden" asp-for="Input.InReplyTo">
    <input type="hidden" asp-for="Input.References">
    <div class="field"><label asp-for="Input.To">To</label><input asp-for="Input.To" type="email" required></div>
    <div class="field"><label asp-for="Input.Subject">Subject</label><input asp-for="Input.Subject" required></div>
    <div class="field"><label asp-for="Input.Body">Message</label><textarea asp-for="Input.Body" rows="14" required></textarea></div>
    <button class="btn btn-primary" type="submit">Send →</button>
  </form>
</main>
```

- [ ] **Step 4: Run tests** — filter `MailboxPages` → all PASS; full `dotnet test` → green.

- [ ] **Step 5: Commit**

```bash
git add src/Coldstart.Web/Areas/Admin/Pages/Mailbox tests/Coldstart.Web.Tests/MailboxPagesTests.cs
git commit -m "feat(mailbox): compose and threaded reply from the admin panel"
```

---

### Task 6: Full verification + live smoke test

**Files:** none new (fixes only if verification finds problems).

- [ ] **Step 1: Full suite**

Run: `dotnet test`
Expected: all four projects green (~165+ tests). Report per-project totals.

- [ ] **Step 2: Live smoke test against the real mailbox**

The user-secrets already hold real credentials (`Mailbox:Host = web0173.zxcs.nl`, interim). Run the app (`ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5178 dotnet run --no-launch-profile` from `src/Coldstart.Web`, background), then verify with curl that `/admin/mailbox` renders the REAL inbox for a logged-in SuperAdmin. If no SuperAdmin login is scriptable, at minimum: assert the route responds 302→login for anonymous, and exercise `ImapMailbox` directly against the real server with a tiny `dotnet run`-able check or by temporarily pointing one GreenMail test at the real host — do NOT commit such a temporary change. Record what was verified in the report.

- [ ] **Step 3: Commit any fixes**

```bash
git add <specific paths>  # only if fixes were needed
git commit -m "fix(mailbox): <what the smoke test surfaced>"
```

# Admin Mailbox — info@ webmail inside the admin panel

**Date:** 2026-07-21
**Status:** Approved by Andrzej (brainstorming session; approach A of three; executed autonomously overnight at his request)

## Problem

Andrzej now has a real mailbox (`info@getcoldstart.nl`, Vimexx Webmail Basic: IMAP/SMTP at `mail.getcoldstart.nl`) and wants ONE place for everything: read incoming mail, reply, and compose new mail (e.g. manual invitations to prospects) from inside the Coldstart admin panel, without switching to a separate mail app.

## Decision

A live-IMAP mini webmail in the Admin area (approach A). The mailbox server remains the single source of truth — no mail is stored in Postgres; every page load reads over IMAP. Resend keeps ALL transactional sending (invites, password resets, welcome, briefings); this feature adds only the human `info@` channel.

## Prerequisite (operational, not code)

The domain's custom Vimexx DNS template still lacks the mail records. Andrzej copies from DirectAdmin → DNS Management into the Vimexx template: apex **MX**, **`mail` A record**, **SPF** TXT. Until then `mail.getcoldstart.nl` does not resolve and the feature has nothing to connect to; code + tests (GreenMail) proceed regardless.

## Architecture

Hexagonal, mirroring the existing email/persistence patterns:

### Application (`src/Coldstart.Application/Mailbox/`)

Port `IMailbox` (all methods take `CancellationToken`):

```csharp
public interface IMailbox
{
    Task<MailboxPage> ListAsync(int page, int pageSize, CancellationToken ct);
    Task<MailMessageDetail?> GetAsync(uint uid, CancellationToken ct);
    Task<MailAttachmentContent?> GetAttachmentAsync(uint uid, int index, CancellationToken ct);
    Task MarkReadAsync(uint uid, bool read, CancellationToken ct);
    Task DeleteAsync(uint uid, CancellationToken ct);           // move to Trash if present, else expunge
    Task SendAsync(OutgoingMail mail, CancellationToken ct);    // sends as Coldstart <info@…> via mailbox SMTP
}
```

Records (public, in the same folder):

```csharp
public sealed record MailboxPage(IReadOnlyList<MailMessageSummary> Items, int TotalCount, int Page, int PageSize);
public sealed record MailMessageSummary(uint Uid, string FromName, string FromAddress, string Subject,
    DateTimeOffset Date, bool Seen, bool HasAttachments);
public sealed record MailMessageDetail(uint Uid, string FromName, string FromAddress, string To, string Subject,
    DateTimeOffset Date, bool Seen, string? HtmlBody /* pre-sanitized by the adapter */, string TextBody,
    string? MessageId, string? References, IReadOnlyList<MailAttachmentInfo> Attachments);
public sealed record MailAttachmentInfo(int Index, string FileName, string ContentType, long Size);
public sealed record MailAttachmentContent(string FileName, string ContentType, byte[] Content);
public sealed record OutgoingMail(string To, string Subject, string TextBody,
    string? InReplyToMessageId = null, string? References = null);
```

`HtmlBody` is sanitized inside the Infrastructure adapter (anti-corruption at the boundary) — Application and Web treat it as safe-to-render.

### Infrastructure (`src/Coldstart.Infrastructure/Mailbox/`)

- `MailboxOptions`: `Host`, `ImapPort = 993`, `SmtpPort = 587`, `Username`, `Password`, `FromName = "Coldstart"`. Bound from configuration section `"Mailbox"`; values live in user-secrets locally and env vars in production. **Never in the repo.**
- `ImapMailbox : IMailbox` using MailKit:
  - Reads: `ImapClient`, connect `Host:ImapPort` SSL-on-connect, `CheckCertificateRevocation = false` (same CRL rationale as `SmtpEmailSender`), open INBOX, newest-first by UID. List = fetch envelopes + flags only; Get = fetch full message.
  - `SendAsync`: MailKit `SmtpClient`, `Host:SmtpPort` STARTTLS, same revocation setting, sets `In-Reply-To`/`References` when provided.
  - Delete: if a folder flagged `\Trash` (or named "Trash") exists → `MoveTo`, else add `\Deleted` + expunge.
- `MailHtmlSanitizer` (internal static): wraps the `HtmlSanitizer` NuGet package (Ganss.Xss). Policy: strip `<script>`, event handlers, `javascript:`/`data:` URLs, all remote images (`img` whose `src` is not `cid:` is removed — v1 shows "remote images removed" note when any were stripped); allowed: basic text/formatting/table tags, `a[href]` forced `target="_blank" rel="noopener noreferrer"`.
- DI: `services.AddScoped<IMailbox, Mailbox.ImapMailbox>()` + options binding in the Web composition root.

### Web (`src/Coldstart.Web/Areas/Admin/Pages/Mailbox/`)

All pages `[Authorize(Roles = ColdstartRoles.SuperAdmin)]` (existing pattern). "Mailbox" link added to the admin nav in `Areas/Admin/Pages/Index.cshtml`'s `app-nav` (and the Mailbox pages carry the same header).

| Page | Route | Behavior |
|---|---|---|
| Inbox | `/admin/mailbox?page=N` | Paged list (25/page), newest first: bold ink row when unseen, FromName (fallback address), subject, `.ts` timestamp, paperclip glyph when `HasAttachments`. Buttons: Refresh (plain GET), Compose. Friendly inline error state when IMAP unreachable. |
| Message | `/admin/mailbox/{uid}` | Header block (from, to, date, subject), body: sanitized HTML when present else `<pre>`-styled text. Attachments list → download links. Actions: Reply (→ compose with `?replyTo=uid`), Mark unread, Delete (confirm `<dialog>`, same pattern as move-error modal). Opening a message marks it read. |
| Compose | `/admin/mailbox/compose[?replyTo=uid]` | Fields To / Subject / plain-text Body. With `replyTo`: prefills `Re: …` subject, quoted original (`On {date}, {from} wrote:` + `> ` lines), and carries threading (`InReplyToMessageId`, `References` = original References + MessageId). Validation: To must parse as a single email address; Subject and Body required. On send failure: inline error, form values preserved. On success: redirect to inbox with a "Sent." flash. |
| Attachment | `/admin/mailbox/{uid}/attachment/{index}` | Streams `MailAttachmentContent` as `File(content, contentType, fileName)`. 404 when out of range. |

Morning Engine styling throughout; monospace only for timestamps. Rendered HTML is wrapped in a `.mail-html` container with CSS `contain: content` and constrained width so foreign styles can't leak into the admin UI.

## Error handling

- Any `IMailbox` operation failing due to connectivity (`SocketException`, MailKit protocol/auth exceptions) surfaces as a friendly inline state ("Mailbox unreachable right now — check the connection and try again"), logged via `ILogger` warning. Pages never 500 on mailbox unavailability.
- Compose/send failure keeps the user's text on screen with an inline error and logs the exception.

## Security

- SuperAdmin-only pages; antiforgery on all POSTs (Razor default).
- HTML sanitized in the adapter (tests below); remote images stripped in v1 (tracking-pixel protection); links `noopener`.
- Credentials via user-secrets/env only. The mailbox password that was shared in chat gets **rotated by Andrzej** after setup.
- Attachment streaming passes through content-type from the mail as-is but always with `Content-Disposition: attachment` (no inline render of foreign files).

## Testing

- **Web** (`Coldstart.Web.Tests`): register `FakeMailbox : IMailbox` in `ColdstartWebFactory` (same pattern as `FakeEmailSender`) — in-memory message list, captures `OutgoingMail`. Tests: inbox lists messages/unread styling/pagination; message view renders fake's HtmlBody and marks read; compose sends and captures To/Subject/Body; reply carries quoted body + threading fields; delete removes; attachment endpoint streams bytes/404s; non-SuperAdmin gets redirected/403 for every route.
- **Infrastructure** (`Coldstart.Infrastructure.Tests`): GreenMail Testcontainer (`greenmail/standalone`) with a preconfigured test user — integration round-trips for `ImapMailbox`: seed via SMTP → List/Get; MarkRead flag round-trip; Delete; SendAsync → received by GreenMail with threading headers. `MailHtmlSanitizer` unit tests: `<script>` stripped, `onerror=` stripped, remote `<img>` removed (and flagged), `javascript:` href neutralized, `cid:` image kept, plain formatting preserved.
- Existing 152-test suite stays green.

## Out of scope (v1)

Folders beyond INBOX (+Trash move), search, HTML composing, auto-refresh/IMAP IDLE, multiple accounts, attachment upload on compose, switching transactional mail away from Resend.

## Consequences

**Positive:** one workflow surface for the operator; mailbox stays authoritative (phone app and panel always agree); clean port means a Postgres-sync upgrade (approach B) later without touching Web; reuses MailKit + existing test patterns.

**Negative / accepted:** every page load pays an IMAP round-trip (~0.5–1.5s to Vimexx); INBOX-only; plain-text composing; feature is inert until the DNS records are added at Vimexx.

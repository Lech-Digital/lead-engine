# Send from Coldstart (branded outreach send) — design

**Date:** 2026-07-23
**Status:** Approved by Andrzej (brainstorming session; implementation greenlit)

## Problem

Today, sending an approved lead's email is a manual handoff: the lead detail offers Gmail / mailto / copy, and the operator sends from their own client, then clicks "Mark as sent." For the operator's own account, Andrzej wants to send the approved draft **directly from the app**, as a real branded email from his Coldstart domain (`andrzej@getcoldstart.nl`), in one click. Customers keep the existing handoff (they should send from their own domains).

## Decision

Add an **admin-only** ("SuperAdmin") action on the lead detail — **"Send from Coldstart"** — that sends the approved subject/draft as a real email from a dedicated outreach mailbox, **deliverability-safe branded** (plain, personal body + a small CSS Coldstart signature; no images, no heavy template), then marks the lead **Sent**. Regular customers never see it.

## Design

### Sending identity & config
- New config section **`OutreachMailbox`** → `OutreachMailboxOptions` (Infrastructure): `Host`, `SmtpPort` (587), `SmtpUseStartTls` (true), `Username` (`andrzej@getcoldstart.nl`), `Password`, `FromName` (`Andrzej — Coldstart`). Bound in `Program.cs` like `Mailbox`/`Llm`. Secrets live in user-secrets (dev) / env (prod), never in the repo.
- Separate from the transactional sender (`SmtpOptions`/`IEmailSender`) and the admin inbox (`MailboxOptions`/`IMailbox`): outreach is a distinct identity and body format.

### Port & implementation
- **`IOutreachMailer`** (Application, `Outreach` folder): `Task SendAsync(OutreachEmail mail, CancellationToken ct)` with `OutreachEmail(string To, string Subject, string Body)` — `Body` is the approved plain draft text.
- **`SmtpOutreachMailer`** (Infrastructure): builds a **multipart/alternative** message:
  - **text/plain:** the draft body verbatim + a plain signature (`— Andrzej\nFounder, Coldstart\nhttps://getcoldstart.nl`).
  - **text/html:** each paragraph of the body as `<p>` (HTML-encoded), followed by a **CSS-only Coldstart signature** block (text `coldstart*` wordmark styled with the brand ink/sunrise tokens, name, "Founder, Coldstart", link). **No `<img>`.**
  - Sends via MailKit STARTTLS on 587 with `CheckCertificateRevocation = false` (same CRL workaround as the other senders). A static `BuildMessage(...)` seam (like `SmtpEmailSender.BuildMessage`) makes composition unit-testable without SMTP.

### Use case
- **`ILeadWorkflow.SendFromColdstartAsync(customerId, leadId, ct)`** (same shape as `MarkSentAsync`): sends the **stored** lead content — "what you approved is what's sent" (edits must be Saved first, exactly like the existing Mark-as-sent path). Load the lead (scoped to the customer); require **Approved + recipient present**; `await outreach.SendAsync(new OutreachEmail(lead.RecipientEmail.Value, lead.Subject, lead.Draft), ct)`; then `lead.MarkSent(now)` and save. Returns `LeadActionResult`:
  - `NotFound` if the lead isn't the customer's.
  - `Invalid` (friendly message, **not** marked sent) if not approved / no recipient / **the send throws** (SMTP failure — caught as `Exception` except `OperationCanceledException`).
  - `Ok` on success.
- The send is attempted **before** `MarkSent`, so a failed send never marks a lead as sent. This method does its own load/ownership/persist (it can't use the sync `MutateAsync` seam because the send is async).

### Web
- New handler **`OnPostSendFromColdstartAsync(Guid id, CancellationToken ct)`** on `Areas/App/Pages/Leads/Detail.cshtml.cs`. **Gate:** if `!User.IsInRole("SuperAdmin")` → return `Forbid()`. Reuses the existing `RunAsync` result handling; on success redirects to the briefing (`Notice` set by `RunAsync`'s existing flow — or a dedicated notice).
- **UI:** in `Detail.cshtml`, inside the existing `approved` send panel, render a **"Send from Coldstart ✓"** button only when `User.IsInRole("SuperAdmin")` (disabled when no recipient), next to the existing "Mark as sent" (which stays for the customer handoff path). Non-admins never see it.

## Behavior (unchanged)
Existing lead actions (Save/Accept/Discard/Reopen/MarkSent/notes), customer handoff (Gmail/mailto/copy), and all ownership/status guards are untouched. `MarkSent` domain rule (Approved + recipient) is reused, not modified.

## Testing
- **`SmtpOutreachMailer.BuildMessage`** — HTML alternative contains the CSS signature + `coldstart` wordmark and **no `<img`**; a text/plain alternative exists; `From` is the configured outreach username; subject/recipient set.
- **`SendFromColdstartAsync`** — happy path (Approved+recipient → fake mailer called once with the stored subject/draft/recipient, lead becomes `Sent`, `SentAt` set); sad paths (not approved → `Invalid`, no recipient → `Invalid`, **mailer throws → `Invalid` and lead stays Approved / not sent**).
- **Detail page** — a SuperAdmin sees the "Send from Coldstart" button and posting the handler (fake `IOutreachMailer`) sends once + marks the lead Sent; a regular customer does **not** see the button and posting the handler returns 403.

## Out of scope
- Bulk send, scheduling, open/click tracking, List-Unsubscribe headers (revisit if cold volume grows and domain reputation becomes a concern — then move cold sending to a subdomain).
- Any change to customer send flow or to transactional/inbox mail.
- Rich HTML marketing template (explicitly rejected for deliverability).

## Consequences
**Positive:** one-click, on-brand outreach from Andrzej's own address; failed sends never corrupt lead state; customers unaffected; deliverability-safe by construction (no images, plain body). **Negative / accepted:** a second SMTP identity to configure and keep secret; the signature is CSS-only (no logo image) by deliberate choice.

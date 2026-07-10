# Admin Console + Branded Transactional Emails — Design

**Date:** 2026-07-10
**Status:** Accepted
**Branch:** `dotnet-port`

## Goal

Turn the SuperAdmin `/admin` page into a real operations console (invite management + a companies/users overview with block and remove), and give the two transactional emails that already send today — the **invitation** and the **password reset** — a branded, deliverability-safe HTML treatment. Outcome: the operator (`adzius.lech@gmail.com`) can log in, send a polished first invitation, and manage accounts from one screen.

## Context (current state)

- `/admin` (`Areas/Admin/Pages/Index`) is gated `[Authorize(Roles = ColdstartRoles.SuperAdmin)]` and already does: an invite form (create `CompanyAdmin` + provision tenant + email a set-password link) and Pending/Accepted lists. Duplicate-email invites are rejected.
- All email is **plain text**: `SmtpEmailSender` (MailKit) emits only `new TextPart("plain")`; the invite body (Admin page) and reset body (`ForgotPassword`) are inline strings.
- `IEmailSender` (`Coldstart.Application.Ports`) = `Task SendAsync(string to, string subject, string body, CancellationToken ct)`. Callers: the Admin invite handler, `ForgotPassword`, and `SubmitInquiry` (contact form).
- Brand (from `wwwroot/logo.svg` + `css/site.css`): cream canvas `#F3E9D6` / card `#FBF4E6`, ink `#382C23`, orange `#E07A3E` + dark `#A04E1E` (shadow `#7a3c16`), rainbow accent (orange→rose `#D9897F`→gold `#E0A93B`→teal `#5E9E94`→blue `#6E6AB0`), monospace wordmark "Cold**start**" with a keycap-"C" mark, tactile 3D-shadow buttons.
- `ApplicationUser`: `CustomerId` (Guid?), `InvitedAt` (DateTimeOffset?), plus Identity's `EmailConfirmed`, `LockoutEnd`, `LockoutEnabled`, `SecurityStamp`. `EmailConfirmed == false` ⇒ invited-not-accepted.

## Global constraints

- Hexagonal/DDD (CS-HEX): Domain has zero external deps; ports live in Application; DI in composition roots. **HTML email building is a Web/presentation concern** — the `IEmailSender` port stays transport-only. Tenant deletion is an Application port with an Infrastructure adapter (no `DbContext`/`IQueryable` in Web business logic, CS-DATA-01).
- Async all the way; `CancellationToken` through IO (CS-DATA-03). `internal` for implementation classes; `record` for DTOs/VOs (CS-MOD-01, CS-DDD-01).
- Build stays at **0 warnings**; full suite green after every task. No `Co-Authored-By` trailers.
- Tests: xUnit + FluentAssertions (+ NSubstitute where a port needs mocking), strict AAA, ≥1 happy + 2 sad paths per unit, via `ColdstartWebFactory` (Testcontainers Postgres) + `TestAuth`.

## Architecture

Two cohesive components under the existing SuperAdmin surface, no new project or layer:

```
Areas/Admin/Pages/Index (SuperAdmin console)
   ├─ invite / resend / revoke / copy-link        → UserManager + IProvisionCustomer + EmailTemplates + IEmailSender
   ├─ block / unblock                              → UserManager (lockout + security stamp)
   └─ remove                                       → UserManager.DeleteAsync + IDeleteCustomer
Coldstart.Web/Email/EmailTemplates (branded shell) → produces (Subject, TextBody, HtmlBody)
Coldstart.Application.Ports/IEmailSender           → transport, now text + optional html
Coldstart.Infrastructure/Email/SmtpEmailSender     → multipart/alternative
Coldstart.Application.Customers/IDeleteCustomer     → Infrastructure adapter deletes tenant + its leads
```

## Component A — Admin console

Single Razor page (`/admin`), reorganized into sections with additional POST handlers (named handlers via `asp-page-handler`).

**Sections**
1. **Invite** — existing company + email form (unchanged).
2. **Companies & users** — one table over all `Customer`s and their `ApplicationUser`s. Columns: Company, Email, Role, **Status**, Invited/Days-left, Actions. Status derived: `Pending` (`!EmailConfirmed`), `Blocked` (`LockoutEnd` in the future), else `Active`. (Pending/Accepted lists from the current page fold into this table.)

**Handlers & behavior**
- `OnPostInviteAsync` — existing invite (kept; now sends the branded invitation email).
- `OnPostResendAsync(email)` — pending only: new reset token → resend invitation email (fresh 7-day link).
- `OnPostRevokeAsync(email)` — pending only: delete the not-yet-accepted `ApplicationUser` and its provisioned (empty) tenant via `IDeleteCustomer`.
- `OnPostCopyLinkAsync(email)` — pending only: generate a set-password token, build the URL, surface it in the page for a client-side copy button (JS `navigator.clipboard`, no-JS fallback = selectable text).
- `OnPostBlockAsync(id)` / `OnPostUnblockAsync(id)` — set `LockoutEnabled=true` + `SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue)` to block / `null` to unblock; on block also `UpdateSecurityStampAsync` to invalidate live cookies.
- `OnPostRemoveAsync(id)` — `UserManager.DeleteAsync(user)` then `IDeleteCustomer.DeleteAsync(customerId)` (removes the tenant + its leads); confirmed via a JS `confirm` / dedicated confirm control. Assumes the current **one-admin-per-tenant** model (each invite provisions its own `Customer`); revisit cascade rules if shared tenants are introduced later.

**Guards** (all enforced server-side, not just UI):
- The SuperAdmin account (role `SuperAdmin` / `ColdstartRoles.SuperAdminEmail`) can never be blocked or removed.
- The acting user cannot block/remove themselves.
- Actions scoped to the correct status (e.g. resend/revoke only for pending) — no-op + surfaced message otherwise.

## Component B — Branded email system

**Transport change (Application + Infrastructure).** Change the port to carry an optional HTML alternative:

```csharp
Task SendAsync(string to, string subject, string textBody, string? htmlBody, CancellationToken ct);
```

`SmtpEmailSender`: when `htmlBody` is non-null, build `new BodyBuilder { TextBody = textBody, HtmlBody = htmlBody }.ToMessageBody()` (yields `multipart/alternative`); otherwise keep the plain `TextPart`. Update the one non-templated caller (`SubmitInquiry`) to pass `htmlBody: null`.

**Branded shell (`Coldstart.Web/Email/EmailTemplates.cs`, `internal`).** Produces:

```csharp
internal sealed record EmailContent(string Subject, string TextBody, string HtmlBody);
internal static class EmailTemplates
{
    internal static EmailContent Invitation(string company, string setPasswordUrl);
    internal static EmailContent PasswordReset(string setPasswordUrl);
}
```

Shared private `Shell(title, introHtml, ctaText, ctaUrl, footnote)` renders the HTML: table-based, inline CSS, 600px max, cream card on cream canvas, ink text. Branded elements, all **image-free** (renders with images blocked; no hosted asset needed pre-deploy):
- **Rainbow accent bar** — a row of five colored `<td>`s (orange/rose/gold/teal/blue). No CSS gradients (Outlook-safe).
- **Logo** — HTML/CSS: a keycap "C" (styled `<td>`/`<span>`: orange bg, rounded, white monospace C) + wordmark "Cold" (ink) + "start" (dark orange), monospace font stack (`Consolas, Menlo, monospace`).
- **CTA button** — bulletproof `<a>` styled as a button: dark-orange bg, white text, rounded, padding (the 3D shadow degrades gracefully where unsupported).
- **Footer** — "Coldstart" + link to `https://getcoldstart.nl`.
- Light-first; acceptable minor variance under client dark-mode auto-inversion.

Each template also returns a clean **plain-text** body (greeting, one line of context, the raw URL, the 7-day note) for the `multipart/alternative` fallback and deliverability.

**Wire-up.** `ForgotPassword` and the Admin invite/resend handlers build the URL as today, call the matching `EmailTemplates` method, and send `content.TextBody` + `content.HtmlBody`.

## Data & persistence

- **No migration.** Block uses Identity lockout; remove uses deletes.
- `IDeleteCustomer` (Application) + Infrastructure adapter: within the `DbContext`, delete the customer's leads then the `Customer` row (respect existing FK/cascade). Returns `Task`, takes `CancellationToken`.

## Error handling & edge cases

- Invite to an existing email → existing "already has an account" model error (kept).
- Resend/revoke/copy on a non-pending or unknown email, block/remove on unknown id, or a guarded target → no state change + a surfaced `StatusMessage`/model error; never a 500.
- Email send failure propagates (as today) — the operator sees the error rather than a false success.
- `IDeleteCustomer` on a null `CustomerId` (e.g. SuperAdmin) → no-op.

## Security

- Whole surface stays `[Authorize(Roles = SuperAdmin)]`.
- Antiforgery on every POST (Razor Pages default; app sets header name for the existing drag-move JS — form posts unaffected).
- Server-side guards (above) are authoritative; the UI merely hides inapplicable actions.

## Testing strategy

- Extend `FakeEmailSender.Sent` to capture the HTML body: `(To, Subject, TextBody, HtmlBody)`. Update existing assertions (they currently read `.Body`).
- **Emails:** invitation + reset each produce a non-empty HTML body containing the set-password URL and the plain-text URL; sent as multipart (both parts present). Assert brand markers (e.g. "getcoldstart.nl", wordmark text) appear in HTML.
- **Admin actions:** resend re-sends (new email captured), revoke deletes the pending user + tenant, block sets lockout and a subsequent login is rejected, unblock restores login, remove deletes user + tenant, and the SuperAdmin/self guards reject. Non-SuperAdmin denied (existing test pattern). xUnit + FluentAssertions, AAA, ≥1 happy + 2 sad per area.

## Out of scope (deferred)

- The two **cold-outreach** templates (email to a lead, operator→customer) — deliverability-safe/near-plain, and their .NET send path isn't built yet.
- Company-profile editing from `/admin`, email-confirmation flow, TOTP/2FA (separate efforts).
- Swapping the CSS logo for a hosted PNG (revisit once `getcoldstart.nl` is deployed).

# Invitation Registration — Spec A: invite-only access, roles, and inviting company admins

**Date:** 2026-07-08
**Status:** Accepted

## Context

Registration today is open self-signup: anyone can POST `/Identity/Account/Register`,
which creates an `ApplicationUser`, provisions a new `Customer` tenant named after their
email, and signs them in. Coldstart is a paid, invite-only product, so open registration
must go away and access must be granted deliberately by an operator.

The full vision is a three-tier, role-based invitation hierarchy:

- **SuperAdmin** (the operator) can do everything and invites company admins.
- **CompanyAdmin** runs one company/tenant and (later) invites their teammates.
- **User** belongs to a company and sees the same leads as the rest of that company.

This is decomposed into two independently-shippable specs. **This document is Spec A.**
Spec B (company admins invite Users into their tenant + a SuperAdmin oversight dashboard
with company/admin/user counts) is a follow-on that reuses this foundation.

Key existing facts that shape the design:

- Identity already uses `AddIdentity<ApplicationUser, IdentityRole>` — **roles are enabled**.
- `IEmailSender` → `SmtpEmailSender` (MailKit, plain text) is already wired and used by the
  contact form. **Email sending is not built here; it is reused.**
- `ApplicationUser.CustomerId` already links a user to a tenant, and the lead read-models
  already query *by* `CustomerId`. Multiple users sharing a `CustomerId` therefore see the
  same leads with **no schema change to leads** (this is what makes Spec B cheap).
- There are **no ForgotPassword / ResetPassword pages** yet. The Identity password-reset
  token is the mechanism this whole flow is built on.

## Decision

Build an invite-only foundation where **every account is created with a random password and
the person sets their real password via an emailed link** (Identity's password-reset token).
"Accept an invite" and "reset your password" are the same underlying mechanism, differing
only in email framing.

### 1. Roles & authorization

- Seed three Identity roles on startup: `SuperAdmin`, `CompanyAdmin`, `User`.
- A `SuperAdmin` authorization policy gates `/admin/*`.
- `/app/*` stays `[Authorize]` (any authenticated user), so both a CompanyAdmin and their
  Users reach the leads.
- SuperAdmin has `CustomerId = null` (not a tenant). CompanyAdmin/User each carry a
  `CustomerId`.

### 2. Super-admin bootstrap

- Hardcoded operator email `adzius.lech@gmail.com` (a constant, not configuration).
- On startup, if that user is missing: create it with a random password,
  `EmailConfirmed = true`, and the `SuperAdmin` role. No tenant.
- The operator sets their password via **Forgot password** — the same emailed-link flow as
  everyone else. No password is stored in config and no auto-email is sent on seed.

### 3. Password-set / reset flow (shared mechanism)

- Add `/Identity/Account/ForgotPassword`:
  - Enter email → `GeneratePasswordResetTokenAsync` → email a reset link via `IEmailSender`.
  - Always render a neutral "if that account exists, we've sent a link" result regardless of
    whether the email matches an account (no account enumeration).
- Add `/Identity/Account/ResetPassword`:
  - Accepts `email` + `token` + new password → `ResetPasswordAsync`.
  - On success: mark `EmailConfirmed = true`, sign the user in, redirect by role
    (`SuperAdmin → /admin`, otherwise `/app`).
- Extend the reset-token lifespan to **7 days** (`DataProtectionTokenProviderOptions.TokenLifespan`)
  so the same token works as the invite window.

### 4. Disable public registration

- `/Identity/Account/Register` GET and POST both redirect to `/Identity/Account/Login`.
- Remove links to Register from the UI (login page, landing page, anywhere else).

### 5. Inviting a company admin

Because accounts are created immediately with a random password, **"invite" = create the
account now**:

1. Create `ApplicationUser` with a random password, `EmailConfirmed = false`, and
   `InvitedAt = now`.
2. Assign the `CompanyAdmin` role.
3. Provision a new `Customer` tenant named from the entered company name
   (reuse `IProvisionCustomer`; today it names the tenant from email — pass the company
   name instead) and set the user's `CustomerId`.
4. Generate a password-reset token and email a set-password link, framed as an invitation
   ("You've been invited to Coldstart as the admin for {company}. Set your password: {link}").

**No separate invitation entity.** Invite state lives on the user:

- "Not yet accepted" = `EmailConfirmed = false`.
- "Accepted" = `EmailConfirmed = true` (set when they set their password).
- `InvitedAt` (new nullable field on `ApplicationUser`) supports the "expires in N days"
  display and reflects the 7-day token window.

Orchestration follows the existing codebase pattern: the `Register` page already calls
`UserManager` + `IProvisionCustomer` directly, so the invite is orchestrated in the `/admin`
page handler (`UserManager` + `RoleManager` + `IProvisionCustomer` + `LinkGenerator` +
`IEmailSender`). Identity user management is an infrastructure/composition concern; the
hexagonal rules continue to apply to Domain/Application/Infrastructure.

### 6. `/admin` surface (SuperAdmin only)

A single page, the SuperAdmin home:

- **Invite form:** company name + admin email → creates a CompanyAdmin invite (§5).
- **List:** pending company-admin invites (`CompanyAdmin` role, `EmailConfirmed = false`)
  with days-left computed from `InvitedAt`, plus accepted ones.

The richer companies/admins/users **counts** dashboard is Spec B (counts only become
meaningful once Users exist).

### 7. Accept = set password

The invited admin clicks the emailed link → `/Identity/Account/ResetPassword` (§3) → sets
password → `EmailConfirmed = true` → signed in → redirected to `/app`.

## Components

- **Web / Identity area:** new `ForgotPassword` + `ResetPassword` pages; `Register`
  disabled; new `Areas/Admin` with an invites page under a `SuperAdmin` policy.
- **Web / composition:** startup seeding of roles + the SuperAdmin user; authorization
  policy registration; token-lifespan configuration; redirect-by-role helper.
- **Infrastructure / Identity:** `ApplicationUser.InvitedAt` field + EF migration.
- **Application:** `IProvisionCustomer` gains/uses an explicit company-name argument (it
  already takes a name — callers currently pass the email).
- **Reused as-is:** `IEmailSender` / `SmtpEmailSender`, `Customer` aggregate, lead read-models.

## Data flow

**Invite:** SuperAdmin submits (company, email) on `/admin` → create user (random pw,
`CompanyAdmin`, `EmailConfirmed=false`, `InvitedAt=now`) → provision `Customer(company)` →
set `CustomerId` → generate reset token → `IEmailSender.SendAsync(email, invite link)`.

**Accept / set password:** click link → `ResetPassword` → `ResetPasswordAsync` → set
`EmailConfirmed=true` → sign in → `/app`.

**SuperAdmin password:** Forgot password → reset token → email → `ResetPassword` → `/admin`.

## Error handling

- ForgotPassword: never reveal whether an email exists; always neutral confirmation.
- ResetPassword: invalid/expired token → clear error, offer to request a new link.
- Invite an email that already has an account: surface a clear "that person already has an
  account" message; do not clobber the existing account or tenant.
- Invalid company name / email on the invite form: standard model validation.

## Testing

Web integration tests (Testcontainers Postgres) with a **fake `IEmailSender`** that captures
the outgoing link:

- Seeded SuperAdmin exists and is in the `SuperAdmin` role after startup.
- `GET /Identity/Account/Register` redirects to Login (public register disabled).
- A non-SuperAdmin hitting `/admin` is denied (redirect/403).
- SuperAdmin posts an invite → a `CompanyAdmin` user is created with a new tenant and
  `EmailConfirmed = false`; a link is emailed.
- Inviting an already-registered email returns the "already has an account" path and does
  not create a duplicate/second tenant.
- Accept: following the captured reset link and setting a password signs the user in and
  flips `EmailConfirmed = true`.
- ForgotPassword for the seeded SuperAdmin emails a link; using it sets the password.

## Consequences

### Positive
- Access is fully deliberate; no open signup.
- One mechanism (Identity reset token) powers bootstrap, invites, and password resets.
- No new domain entity and no leads schema change; Spec B (teammates) reuses this and the
  existing per-`CustomerId` lead visibility.

### Negative
- Accounts (and their tenants) exist before acceptance; an unaccepted invite leaves a
  password-unusable account + an empty tenant. Acceptable for Spec A; revoke/cleanup can be
  added later if it matters.
- Real email delivery depends on `SmtpOptions` being configured in the environment; this
  spec does not build or provision SMTP.
- Invite orchestration lives in the `/admin` page handler (matching the existing `Register`
  pattern) rather than a dedicated Application use-case.

## Out of scope (Spec B)

- CompanyAdmins inviting Users into their tenant.
- SuperAdmin oversight dashboard with company/admin/user counts.
- Invite revocation / resend / cleanup of unaccepted accounts.

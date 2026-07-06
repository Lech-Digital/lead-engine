# Coldstart .NET — Zone & Landing Polish (Stage A)

**Date:** 2026-07-06
**Status:** Proposed
**Author:** Andrzej Lech (with Claude)

## Context

Builds on the customer zone (`/app`, real leads + card dashboard) and the Razor landing. Three focused improvements:
1. The landing's "Log in" is only in the scroll-triggered statusbar, so logging in requires scrolling — fix it.
2. The customer zone should show an editable **company profile** (company name, address, VAT, KvK, niche, website, contact) — the operator fills it in the browser.
3. Give the zone a proper, better-looking app shell.

**Out of scope — deferred to Stage B** (the next stage the user chose to follow this): the **admin console** (add/edit/hold/remove customers), **disabling public registration**, and the **invite-link → set-password** onboarding flow, plus the auth-hardening items (email confirmation, OTP/2FA) which fold into that invitation flow. Public self-registration stays live through Stage A. `CompanyProfile` built here is deliberately the shared foundation Stage B's admin editing will reuse.

## Goals

1. "Log in" (→ "Customer zone →" when authenticated) reachable on the landing without scrolling.
2. Editable company profile in the customer zone (Customer-owned fields + an edit form).
3. A cohesive branded `/app` shell (app-bar, profile summary, refined leads grid).

## Non-goals

Admin console; customer CRUD/hold/remove; disabling registration; invitations/set-password; email confirmation; OTP/2FA; roles/permissions. (All Stage B or later.)

## Design

### A. Landing — "Log in" without scrolling
Add a persistent link in the hero's top row (beside the Coldstart logo in `Pages/Index.cshtml`): **"Log in"** → `/Identity/Account/Login`, rendered as **"Customer zone →"** → `/app` when `User.Identity?.IsAuthenticated == true`. Visible on first paint. The existing scroll statusbar link is unchanged. Small CSS for the header row placement.

### B. Company profile (editable in-app)
- **Domain:** a `CompanyProfile` value object (`record CompanyProfile(string? CompanyName, string? Address, string? VatNumber, string? KvkNumber, string? Niche, string? Website, string? ContactEmail)`; a static `CompanyProfile.Empty`). `Customer` gains `CompanyProfile Profile { get; private set; }` (default `Empty`) and `void UpdateProfile(CompanyProfile profile)` (behaviour, not a setter). `Customer.Name` remains the login email (identity key); `Profile.CompanyName` is the display name.
- **Persistence:** EF **owned-type** mapping of `CompanyProfile` on `Customer` (columns nullable). New migration `AddCompanyProfile`.
- **Application:** `ICustomerRepository.GetAsync(CustomerId, ct) : Task<Customer?>` (by id) + a save (`UpdateAsync` or `SaveChangesAsync` via the tracked entity). Use case `IUpdateCompanyProfile.UpdateAsync(Guid customerId, CompanyProfileInput input, ct)` (loads, `UpdateProfile`, saves). A `CompanyProfileView` read-model (record mirroring the fields) so the Web layer stays entity-free; surfaced via a `IGetCompanyProfile.ForCustomerAsync(Guid, ct)` use case (or folded into the zone's inbox call).
- **Web:** the zone shows a **Company profile card** — filled values, or an **"Add your company details"** empty state with a link to edit. An edit page **`/app/profile`** (`[Authorize]`, antiforgery) with a form for the 7 fields → POST → `IUpdateCompanyProfile` → back to `/app`. All fields optional; light validation (e.g. website/email format only if provided).

### C. Zone shell polish
Restructure `Areas/App/Pages/Index.cshtml` into a branded dashboard: a slim **app-bar** (Coldstart mark · a "Company profile" link · Log out), a **company-profile summary** panel at the top, then the refined Stage-4 **leads grid** below. Brand palette from `site.css`; new styles appended.

## Data flow
`/app` OnGet → load the signed-in user's `CustomerId` → `IGetCompanyProfile.ForCustomerAsync` (profile) + `IGetLeadInbox.ForCustomerAsync` (leads) → render app-bar + profile summary + leads. `/app/profile` POST → `IUpdateCompanyProfile.UpdateAsync(customerId, input)` → redirect to `/app`.

## Error handling
- Null `CustomerId` / customer not found → zone shows the empty profile + empty leads state (no crash).
- Profile form: invalid website/email (when provided) re-renders the form with a message; all fields blank is allowed (empty profile).

## Testing (strict AAA; Testcontainers for DB-backed; build 0 warnings; no commit trailer)
- **Domain:** `CompanyProfile.Empty`; `Customer.UpdateProfile` replaces the profile (happy + updating twice).
- **Application:** `UpdateCompanyProfile` (mocked repo) loads → updates → saves; unknown customer id handled. `GetCompanyProfile` maps `Customer.Profile` → `CompanyProfileView`.
- **Infrastructure:** EF owned-type round-trip (Testcontainers) — save a customer with a filled profile, read it back; `GetAsync` by id.
- **Web (WebApplicationFactory + Testcontainers):** zone shows the empty-profile state for a fresh customer; after POSTing `/app/profile`, the values persist and render in `/app`; `/app/profile` requires auth (unauth → login). Landing GET `/` contains the hero "Log in" link (i.e. present without the statusbar's scroll class).

## Open items / assumptions (confirm on review)
- Profile fields = `CompanyName, Address, VatNumber, KvkNumber, Niche, Website, ContactEmail` (all optional). KvK + VAT reflect the Dutch business context.
- Edit form at `/app/profile`; profile summary shown on `/app`.
- Public registration stays live this stage (replaced by invitations in Stage B).

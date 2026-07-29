# Admin API + CLI — Design

**Date:** 2026-07-29
**Status:** Approved

## Purpose

The operator (SuperAdmin) can currently administer Coldstart only through the `/admin`
web console. With the Hetzner deploy imminent, we want the same administration from a
terminal — both by the operator directly and by Claude via Bash — without SSH-ing into
the box or hand-writing SQL. v1 covers: customers & users, health & stats, on-demand
harvest triggering, and read-only billing visibility.

## Approach

An **admin HTTP API** (`/api/admin/*`, minimal APIs in `Coldstart.Web`) plus a **thin
CLI** (`src/Coldstart.Cli`) that is a pure HTTP client of that API. The CLI never
touches the database or app internals, so the same binary administers local dev and
production by switching a base URL. No MCP server in v1 — Claude drives the CLI through
Bash; an MCP wrapper over the same API remains possible later.

Rejected alternatives: SSH-wrapper CLI (domain actions would bypass app guards via raw
SQL); cookie-login CLI (antiforgery/session pain); JWT bearer (overkill for a single
operator).

## Authentication

- Static admin API key sent as an `X-Admin-Key` header.
- Checked by an **endpoint filter on the `/api/admin` route group**, constant-time
  comparison (`CryptographicOperations.FixedTimeEquals`) against `AdminApi:Key` from
  configuration (user-secrets locally, `deploy/.env.prod` in production).
- **Fail closed:** when `AdminApi:Key` is unset or empty, every `/api/admin/*` request
  returns 404 (the feature is off, not open).
- Endpoints are `AllowAnonymous` at the ASP.NET auth layer (the filter is the gate), so
  the cookie login redirect never applies.
- Transport security is Caddy's HTTPS in production.
- If staff are ever added, the filter is swapped for a real authentication scheme
  without touching endpoint code.

## Endpoints

All under one `MapGroup("/api/admin")` with the key filter. JSON in/out.

| Method & path | Behaviour |
|---|---|
| `GET  /api/admin/customers` | All companies with their users: user email, status (Pending/Blocked/Active), invite days-left, plan, plan-renews-at, Stripe customer/subscription presence, lead counts (total / pending review), `HarvestStatus`, `LastHarvestAt`. Covers "customers & users" and "billing visibility". |
| `POST /api/admin/invites` | Body `{ email, company, niche? }`. Same flow as the web console: create user (unconfirmed, random password) → role `CompanyAdmin` → optional AI search-profile seed → provision tenant → send branded invite email → enqueue first harvest. Returns the created customer id + set-password link. |
| `POST /api/admin/invites/{email}/resend` | Re-send a pending (unconfirmed) invite. |
| `DELETE /api/admin/invites/{email}` | Revoke a pending invite: delete user + tenant. |
| `POST /api/admin/users/{email}/block` | Lockout-to-max + security-stamp rotation (kills live cookies). |
| `POST /api/admin/users/{email}/unblock` | Clear lockout end. |
| `DELETE /api/admin/users/{email}` | Remove user + their tenant (cascades leads). |
| `GET  /api/admin/stats` | DB health, customer count, per-customer harvest status summary, counts of pending invites / blocked users. The "is everything OK" morning glance. |
| `POST /api/admin/harvest/{customerId}` | `IHarvestQueue.Enqueue(new HarvestJob(id))` — fire-and-forget, returns 202. |
| `POST /api/admin/harvest/due` | Run the nightly sweep logic now via `NightlyHarvestScheduler.EnqueueDueAsync`; returns the enqueued count. |

Error shape: RFC 7807 `ProblemDetails` (400 validation, 404 unknown email/customer,
409 e.g. resend on a confirmed user).

## Key refactor: `AdminUserService`

The invite/resend/revoke/block/unblock/remove logic currently lives inline in
`Areas/Admin/Pages/Index.cshtml.cs` (287 lines of `UserManager` calls). Extract it into
an `internal` **`AdminUserService`** in `Coldstart.Web` (it depends on ASP.NET Identity,
so it must not sink into the Application layer — CS-HEX rules), shared by the Razor page
and the new endpoints. Carries over unchanged:

- `GuardTargetAsync` — refuses SuperAdmin targets and self-targeting.
- Set-password-link building — rewritten from `Url.Page` to `LinkGenerator` so it works
  from both PageModel and minimal-API contexts.
- The 7-day invite window / status projection (`LoadAsync` logic) becomes the shared
  read model for both the page and `GET /customers`.

The Razor page keeps its handlers but delegates to the service — behaviour identical,
verified by the existing `AdminConsoleTests` / `AdminInviteTests` staying green.

Cross-tenant reads use the `IgnoreQueryFilters` repository methods
(`ICustomerRepository.AllAsync`) or `ITenantScope.SetCurrent` where a tenant-scoped use
case is reused — the EF tenant filter is fail-closed and the caller's own claim is the
"Coldstart" tenant, not the target.

## CLI (`src/Coldstart.Cli`)

- New console project (6th in the solution), modelled on `Coldstart.Harvester`'s host
  but with no Infrastructure reference — only `HttpClient` + `System.Text.Json`.
- Config from env vars: `COLDSTART_URL` (default `http://localhost:5000`),
  `COLDSTART_ADMIN_KEY`. Missing key → clear error, exit 2.
- Commands:
  - `coldstart customers` — table of companies/users/status/plan/leads/last-harvest
  - `coldstart invite <email> --company <name> [--niche <text>]`
  - `coldstart resend <email>` / `revoke <email>`
  - `coldstart block <email>` / `unblock <email>` / `remove <email>` (remove asks
    for confirmation unless `--yes`)
  - `coldstart stats`
  - `coldstart harvest <company-or-customer-id>` / `coldstart harvest --due`
- `--json` on every read command prints the raw API response for machine consumption
  (Claude via Bash).
- Exit codes: 0 success, 1 API/HTTP error (body printed to stderr), 2 usage/config error.
- Plain `System.CommandLine`-free argument parsing (a small internal dispatcher) to keep
  dependencies at zero; revisit only if the command surface grows.

## Testing

- **Integration (`Coldstart.Web.Tests`, existing `ColdstartWebFactory`):**
  - Auth (key configured): request without the header → 401; wrong key → 401;
    right key → 200.
  - Key unset in config → every `/api/admin/*` route returns 404.
  - `GET /customers` shape; invite happy path (fake `IEmailSender` captured, fake
    `IHarvestQueue` received the job); block → user cannot sign in; revoke/remove
    delete the tenant; guard rails (SuperAdmin/self) → 403.
  - Harvest endpoints: enqueue observed on `FakeHarvestQueue`; `/due` returns a count.
- **Regression:** existing `AdminConsoleTests`/`AdminInviteTests` must stay green after
  the `AdminUserService` extraction.
- **CLI unit tests** (new `tests/Coldstart.Cli.Tests`): argument dispatch, exit codes,
  table/JSON formatting against canned API responses (HttpClient with a stub handler).
- Standards: xUnit + NSubstitute + FluentAssertions, AAA comments, 1 happy + 2 sad
  paths minimum per class.

## Out of scope (v1)

- MCP server (Claude uses the CLI via Bash).
- Plan/billing *changes* from the CLI (all writes keep going through Stripe).
- Lead-level operations (approve/discard) — the customer does those in the app.
- Multi-operator auth, rate limiting, audit log.

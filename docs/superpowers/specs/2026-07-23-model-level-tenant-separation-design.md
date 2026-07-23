# Model-level tenant separation — design

**Date:** 2026-07-23
**Status:** Approved by Andrzej (brainstorming session)

## Problem

Tenant isolation today is **application-level and opt-in**: every use case passes a `customerId`, and `LeadWorkflow.MutateAsync` compares `lead.CustomerId` before mutating. Repositories don't scope by tenant (`LeadRepository.GetAsync(leadId)` loads any lead; `ExistsByFingerprintAsync` checks **all** tenants). One forgotten check leaks another tenant's data. Two further correctness gaps for a real multi-customer SaaS: lead dedup is **global** (an `IX_leads_Fingerprint` unique index across all tenants), so the same company/post can only ever be one tenant's lead.

## Decision

Make isolation the **persistence-layer default** via EF Core global query filters driven by an ambient tenant context, and fix dedup to be **per-tenant**. Reads fail closed; a missing app-level check can no longer leak. (Postgres RLS is deferred — see Out of scope.)

## Design

### 1. Ambient tenant context
- **Port** `ITenantContext { Guid? CustomerId { get; } }` in `Coldstart.Application/Ports`.
- **Web:** `HttpTenantContext` (scoped) reads a `customer_id` claim from the signed-in principal via `IHttpContextAccessor`. A custom `AppUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser>` stamps that claim at sign-in from `user.CustomerId`. Tenant resolution is thus a **claim read — no DB hit, no re-entrancy** into `ColdstartDbContext` (resolving via `UserManager` inside a filter would recurse).
- **Harvester / background:** `MutableTenantContext` (settable `CustomerId`) so a run can set the resolved target tenant.
- Registered per host: Web binds `HttpTenantContext`; the Harvester binds `MutableTenantContext`. Both satisfy `ITenantContext`.

### 2. Global query filters (`ColdstartDbContext`)
- The context takes `ITenantContext` and exposes `Guid? CurrentCustomerId => _tenant.CustomerId`.
- `Lead` → `HasQueryFilter(l => l.CustomerId == /* CurrentCustomerId */)`. When no tenant is set, the filter matches **nothing** (fail closed). Owned `LeadNote`s load with their parent `Lead`, so they inherit isolation with no separate filter.
- `Customer` → `HasQueryFilter(c => c.Id == /* CurrentCustomerId */)` (a tenant sees only itself).
- `Inquiry` (public contact form) → **no filter** (not tenant-owned).
- **Implementation note:** `CustomerId` / `Customer.Id` are value-converted keys (`HasConversion`). The exact translatable filter expression (compare on the converted column / raw `Guid`, using a context field EF re-reads per query) is pinned during TDD; the plan's first task proves a filtered read in an integration test before wiring the rest.

### 3. Per-tenant dedup + index change
- Migration: **drop** the global unique `IX_leads_Fingerprint`; **add** a composite partial unique index on `(CustomerId, Fingerprint) WHERE Fingerprint <> ''`. Two tenants can now each own the same fingerprint.
- `ILeadRepository.ExistsByFingerprintAsync(fingerprint, ct)` keeps its signature; it queries `db.Leads` and so becomes **per-tenant automatically** once the filter is active and the caller (harvester) has set the tenant.

### 4. Explicit system/admin bypasses
The only intentional lifts of isolation, each an explicit `IgnoreQueryFilters()`:
- `CustomerRepository.GetByNameAsync` — provisioning (`ProvisionCustomer`) and login lookups run before any tenant is established.
- `CustomerRepository.AllAsync` — the admin console lists every tenant.
- (Inserts are unaffected by read filters; `AddAsync`/`Lead.CreateHarvested` set `CustomerId` explicitly as they already do.)

### 5. Harvester reordering
`HarvestLeads.RunAsync` currently dedups in Phase 1 **before** it resolves the target customer in Phase 2. Reorder so it resolves the target customer first (unfiltered `GetByNameAsync`), sets `MutableTenantContext.CustomerId`, and then runs harvest/dedup/save — so `ExistsByFingerprintAsync` and all reads are scoped to that tenant.

### 6. App-level checks stay
`LeadWorkflow.MutateAsync`'s ownership compare and the pages' `user.CustomerId` scoping remain as defense-in-depth. With the filter, a cross-tenant `GetAsync` already returns `null` → `NotFound`; the explicit check is now redundant for reads but kept intentionally.

## Testing
Integration (Testcontainers Postgres, real filters) unless noted:
- **Isolation:** with tenant A's context, `LeadRepository.GetAsync(B's leadId)` returns `null`; A's briefing/detail never shows B's leads.
- **Per-tenant dedup:** the same fingerprint inserts successfully for customer A **and** customer B (composite index); `ExistsByFingerprintAsync` returns true only within the active tenant.
- **Admin bypass:** `CustomerRepository.AllAsync` returns customers across tenants; `GetByNameAsync` finds a customer with no tenant set (provisioning path).
- **Claim resolution:** a signed-in user's `customer_id` claim is present and `HttpTenantContext` surfaces it (web-level test).
- **Harvester:** a run writes leads only under the resolved target customer, and re-running dedups within that tenant.
- **Regression:** the full existing suite stays green (app-level behavior unchanged for the single active tenant).

## Out of scope
- **Postgres Row-Level Security** — deferred hardening; revisit if we need DB-enforced isolation against raw SQL.
- Filtering Identity/`AspNetUsers` (users/admins span tenants).
- Any UI or product-behavior change; no change to the Send-from-Coldstart feature (independent).

## Consequences
**Positive:** isolation is the persistence default (fail closed); dedup is finally correct for concurrent tenants; the risky surface (raw repo reads) is guarded centrally; app-level checks remain as a second layer. **Negative / accepted:** a value-converted-key query filter needs care to translate; every genuinely cross-tenant path must opt out explicitly with `IgnoreQueryFilters` (few, enumerated); a new claim is added at sign-in (stale only until next login if a user's `CustomerId` ever changes — it's set at creation/invite, so stable in practice).

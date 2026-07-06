# Coldstart .NET — Customer Zone: real leads + redesign

**Date:** 2026-07-06
**Status:** Proposed
**Author:** Andrzej Lech (with Claude)

## Context

The `/app` customer zone (Areas/App/Pages/Index) shows the logged-in user's `Customer`'s leads via `IGetLeadInbox.ForCustomerAsync(user.CustomerId)`. Two problems:
1. On registration `ProvisionCustomer` seeds **3 mock leads** into each user's customer — so the zone shows fakes.
2. The **real** harvested leads (Stage 3) save to a separate house "Coldstart" customer, so the logged-in operator never sees them.
Also the zone view is a plain, unstyled table.

This stage makes the logged-in operator's zone show the **real harvested leads**, and redesigns it into a proper branded dashboard. Single-niche dogfooding; per-user tenant isolation is preserved.

## Goals

1. Remove mock-lead seeding; the operator's zone shows the real harvested leads.
2. Point the harvest at the operator's own account customer (config), so registration + harvest converge on one customer without losing per-user isolation.
3. Redesign the `/app` leads view into a branded, readable lead dashboard (+ a real empty state).

## Non-goals

Multi-niche / per-customer harvesting; the FIT:NO-persistence follow-up (separate); any change to harvesting logic itself.

## Design

### A. Tenancy — converge the operator's login-customer and the harvest-target customer
- **`ICustomerRepository.GetByNameAsync(string name, CancellationToken)`** added (replaces the `AllAsync` + in-memory scan in both call sites — also closes a Stage-3 minor).
- **`ProvisionCustomer`**: **get-or-create by name** (name = the user's email). On registration, if a customer with that name already exists (e.g. one the harvester created), link `user.CustomerId` to it; else create. **No mock leads seeded.**
- **`HarvestLeads`**: the target customer comes from **`NicheOptions.TargetCustomerName`** (config) instead of a hard-coded "Coldstart"; resolved via the same **get-or-create by name**. Set `TargetCustomerName` to the operator's login email in the Harvester `appsettings`.
- Result: the operator registers (customer = their email) and runs the harvester (target = their email) → both resolve to the **same** customer → real leads appear in their `/app`. Other users still get their own (empty) customer — isolation intact.

### B. `/app` redesign (branded lead dashboard)
Rebuild `Areas/App/Pages/Index.cshtml` using the existing brand system (`site.css` palette: cream/ink, orange/teal/gold accents; shared `_Layout`):
- **Header:** greeting/customer name, a lead **count**, and a subtle "updated from your morning harvest" line; a top-right link back to the site / a logout affordance.
- **Lead cards** (one per lead, replacing the raw table row) showing:
  - **Company** (card title) + a color-coded **FIT badge** (Yes = teal, Maybe = gold).
  - A **source chip** (hackernews / funding) and the **score** (monospace).
  - The **subject** line (emphasized).
  - A **draft preview** — first ~2 lines, with an expand (`<details>`) to read the full drafted email.
  - **Meta row:** recipient email + a **confidence** indicator, and status.
- **Empty state:** when the customer has no leads yet, a friendly branded panel ("No leads yet — your next harvest will fill this in.") instead of an empty table.
- New read-model fields exposed for the view: extend `LeadSummary` (or add a `LeadCard` read model) with `Subject`, `DraftPreview`/`Draft`, `Source`, `Score` — the `Lead` already carries these (Stage 3 provenance); `GetLeadInbox`/`IListLeads` map them through so the **Web layer still sees only Application read-models**, never Domain entities.

### C. Data flow
`/app` OnGet → `IGetLeadInbox.ForCustomerAsync(user.CustomerId)` → `IListLeads` maps `Lead`→ the richer read-model (incl. subject/draft/source/score) → cards render. Harvester run → leads saved under the `TargetCustomerName` customer → same customer the operator's login resolves to → visible in `/app`.

## Error handling
- Fresh customer / null `CustomerId` → the empty-state panel (no crash).
- `GetByNameAsync` returns null → get-or-create creates the customer (both call sites).

## Testing (per standards; strict AAA; Testcontainers where DB-backed; build 0 warnings)
- **Update `ProvisionCustomerTests`:** provisioning creates-or-links a customer by email and sets `CustomerId`, and **seeds no leads** (assert `Leads` empty). Add a get-or-create case: provisioning twice / an existing same-name customer → links to the existing one (no duplicate).
- **Update `CustomerZoneTests`:** stop relying on seeded mocks. Register a user, **insert real harvested-style leads into that user's customer via the repository**, then assert `/app` renders them (company, subject, source) and the **empty state** shows for a user with no leads; keep the **tenant-isolation** assertion (user B doesn't see user A's leads) by inserting distinct leads per customer.
- **New:** `ICustomerRepository.GetByNameAsync` repo test (Testcontainers); a `HarvestLeads` test that leads save under the configured `TargetCustomerName` customer.
- **Read-model mapping** unit test: `Lead` → read model carries subject/draft/source/score; no Domain type leaks to Web (compile-time — Web references only Application read-models).

## Open items / assumptions (confirm on review)
- **Tenancy = Option 1** (harvest into the operator's account customer; keep per-user isolation; no mocks). (assumed)
- `NicheOptions.TargetCustomerName` defaults to the operator's login email; set in the Harvester appsettings/user-secrets.
- Redesign uses the existing brand palette (no new design language); a lead's full draft is shown via an expander, not a separate page.

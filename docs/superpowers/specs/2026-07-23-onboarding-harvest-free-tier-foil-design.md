# Onboarding Harvest & Free-Tier Foil — Design

**Date:** 2026-07-23
**Status:** Accepted
**Branch:** dotnet-port

## Context

Today, inviting a customer collects only Company + Email and provisions a bare tenant
with an empty `CompanyProfile` and zero leads. The prospect's niche/needs — already
captured by the public inquiry form (`Niche`, `WhatYouSell`, `Message`) — is emailed to
the operator and then thrown away. A new user therefore logs in to an empty briefing and
has to wait for a manual, console-only harvest that is targeted at Coldstart's *own*
niche (funded startups), not theirs.

We want a new customer to open their app and immediately see **real, niche-relevant
leads** — but only as many as we promise for free (3). The rest are visible but blinded
behind a "foil", as a conversion teaser. Payment/subscription is **not** built here; the
design leaves a clean seam (`Customer.Plan`) for it later (likely Stripe).

There is currently **no** plan/quota/tier concept anywhere in the domain, application, or
config. This design introduces the minimum needed.

## Goal

When an admin invites a customer, capture their niche/info, generate a per-customer
search profile, send the invitation immediately, and run a background harvest so the new
user sees niche-relevant leads on first login — the first 3 unlocked, the remainder
blinded behind an upgrade CTA.

## Key Decisions (resolved during brainstorming)

1. **Invite source:** Both — invite *from an inquiry* (prefills Company/Email/Niche/Info,
   editable) **and** a blank *manual* invite with new Niche + Info fields.
2. **Niche targeting:** Generate a **per-customer search profile** (LLM turns niche+info
   into keywords/buying-signals/offer/disqualifiers). Scoring uses these against the
   existing sources.
3. **Harvest timing:** **Background via an in-process hosted queue** — one job at a time
   (respects Groq rate limits). Invite + email never block on the harvest.
4. **Free quota:** **Fixed global `3`**, in config, not editable per-customer.
5. **Foil behavior:** **Blurred cards + an "Unlock all leads" CTA** showing the true
   remaining count. CTA is placeholder copy now, structured to gate on a subscription
   later.
6. **`Customer.Plan` defaults to `Free`;** the operator's own (superadmin) tenant is
   seeded `Full`. Acceptable that pre-existing invited tenants become free/foiled on
   ship, since we are not live with real customers.

## Non-Goals / Explicit Limitations

- **Sources are unchanged** (Hacker News + funding feeds). Per-customer keywords make
  *scoring* relevant, but a customer whose prospects never appear on those feeds will get
  thin results. This is a sources limitation, out of scope here.
- **No payment/Stripe.** The upgrade CTA is a placeholder; `Customer.Plan` is the seam.
- **No per-customer quota override.** Global `3`.
- Search-profile generation is **one-shot at invite** (persisted). No re-generation UI in
  this feature (admin can already edit the profile fields via the existing profile editor
  once persisted).

## Architecture Overview

```
Admin invite (from inquiry | manual)
      │  Company, Email, Niche, Info
      ▼
Provision tenant ──► CompanyProfile { Niche, Brief, + generated SearchProfile }
      │                    ▲
      │         ISearchProfileGenerator (LLM)  ── niche+info ─► Offer, Keywords, Signals, Disqualifiers
      ▼
Create invited user + send invitation email  (IMMEDIATE — never blocks)
      │
      ▼
Enqueue HarvestJob(customerId) ──► Channel<HarvestJob> ──► HarvestQueueService (BackgroundService, 1 at a time)
                                                                  │
                                                                  ▼
                                              HarvestLeads.RunForCustomerAsync(customerId, profile)
                                                                  │  leads saved (tenant-scoped)
                                                                  ▼
New user logs in ──► inbox: top 3 unlocked in full, remainder blurred + "Unlock all" CTA
```

## Components

### 1. Domain: `Customer.Plan`

- Add `CustomerPlan` enum: `Free`, `Full`.
- `Customer` exposes `Plan` with `private set`; `Customer.Create(name)` defaults to
  `Free`. Add behaviour `Customer.SetPlan(CustomerPlan)` (no raw setter — CS-DDD-02).
- EF: map as a string/int column; migration adds `Plan` with default `Free`.
- Seeding: the superadmin's own tenant (`IdentitySeeder`, currently provisions "Coldstart")
  is set to `Full`.

### 2. Domain: `CompanyProfile` search-profile fields

`CompanyProfile` (record VO) already has `CompanyName, Address, VatNumber, KvkNumber,
Niche, Website, ContactEmail, Brief, MentionSourceTool`. Add:

- `Offer` (string)
- `TargetKeywords` (string — newline/comma-joined; kept simple, no owned collection)
- `BuyingSignals` (string)
- `Disqualifiers` (string)

These are populated by the generator at invite time and persisted. `CompanyProfile.Empty`
extends to empty strings for the new fields. EF: add nullable text columns via migration;
`OwnsOne(Profile)` config extended.

### 3. Application: search-profile generation

- **Port** `ISearchProfileGenerator` (Application/Ports):
  ```csharp
  public interface ISearchProfileGenerator
  {
      Task<SearchProfile> GenerateAsync(SearchProfileRequest request, CancellationToken ct);
  }
  public sealed record SearchProfileRequest(string Niche, string Info);
  public sealed record SearchProfile(
      string Offer, string TargetKeywords, string BuyingSignals, string Disqualifiers);
  ```
- **Impl** `GroqSearchProfileGenerator` (Infrastructure) — one LLM call, parses a JSON
  response into `SearchProfile`. On parse/HTTP failure it throws; the caller (invite)
  catches and proceeds without a profile (harvest becomes a no-op, state = `Failed`).

### 4. Application: provisioning takes niche/info

- Extend the provisioning path so invite can pass the profile data. Reuse the existing
  `CompanyProfileInput` shape where possible. New/adjusted method:
  ```csharp
  Task<Guid> ProvisionAsync(string customerName, CompanyProfileSeed? seed, CancellationToken ct);
  ```
  where `CompanyProfileSeed` carries Niche + Info + the generated `SearchProfile`. Existing
  name-only callers keep working (seed `null` → empty profile, as today).

### 5. Web: invite flow (`Areas/Admin/Pages/Index`)

- **InputModel** gains `Niche` (required) and `Info` (free text: WhatYouSell + Message).
- **Invite from inquiry:** the admin page lists open inquiries (`IInquiryRepository`);
  selecting one prefills **Email**, **Niche**, and **Info** (`WhatYouSell` + `Message`)
  from that inquiry. The inquiry has **no company field**, so **Company is always typed by
  the admin** (the inquiry `Name` is a person, not a company). All prefilled fields remain
  editable.
- **`OnPostInviteAsync`** sequence:
  1. Reject if email already has an account (as today).
  2. `profile = await generator.GenerateAsync(new(Niche, Info), ct)` — wrapped so failure
     does not abort the invite (log, continue with a null profile).
  3. `customerId = await provision.ProvisionAsync(Company, seed(Niche, Info, profile), ct)`.
  4. Create `ApplicationUser`, add `CompanyAdmin` role, set `CustomerId`, save.
  5. `await SendInviteAsync(...)` (unchanged).
  6. `harvestQueue.Enqueue(new HarvestJob(customerId))`.
- The handler returns as fast as today; the harvest is entirely background.

### 6. Application/Infrastructure: harvest queue

- **Port** `IHarvestQueue` (Application):
  ```csharp
  public interface IHarvestQueue { void Enqueue(HarvestJob job); }
  public sealed record HarvestJob(Guid CustomerId);
  ```
- **Impl** `HarvestQueue` (Infrastructure) — wraps an unbounded
  `Channel<HarvestJob>`; `Enqueue` writes, exposes a `ChannelReader` for the service.
  Registered **singleton**.
- **`HarvestQueueService : BackgroundService`** (Infrastructure/Web host) — reads the
  channel in a loop, `await`s each job to completion before the next (sequential →
  rate-limit friendly). For each job: create a DI scope, set tenant scope to the job's
  customer, load the customer + its stored search profile, mark state `Running`, run the
  harvest, mark `Done`/`Failed`. Exceptions are caught per-job (one bad job never kills
  the service).

### 7. Application: per-customer harvest

- **`HarvestLeads` refactor.** Today `RunAsync(ct)` resolves the target by
  `NicheOptions.TargetCustomerName` and builds targeting from global `NicheOptions`. Split
  targeting from sources:
  - Add `RunForCustomerAsync(Guid customerId, HarvestTargeting targeting, CancellationToken ct)`
    where `HarvestTargeting` carries `Offer, TargetKeywords, BuyingSignals, Disqualifiers,
    SenderName, SenderTitle, MinScore, MaxEnrichPerRun` (the per-customer/targeting part of
    today's `NicheOptions`).
  - Global **source** config (HN feeds, funding feeds, lookback, weights) stays on
    `NicheOptions`/source options and is injected as today.
  - The existing console path (`RunAsync`) becomes a thin wrapper: build `HarvestTargeting`
    from the global `NicheOptions`, resolve the target customer by name, call
    `RunForCustomerAsync`. **Console harvester behavior is unchanged.**
  - The queue service builds `HarvestTargeting` from the customer's stored `CompanyProfile`
    search fields (+ SenderName/Title from the profile).
- `NicheOptions` must now be resolvable in the **Web host** (it is only registered in the
  Harvester today). Register a default `NicheOptions` (source config) in Web DI bound from
  the `"Niche"` config section, so `HarvestLeads` resolves. Targeting comes per-customer,
  not from config.

### 8. Domain/Infra: harvest state

- Per-customer harvest status so the inbox can show progress. Add to `Customer`:
  `HarvestStatus` enum (`None`, `Pending`, `Running`, `Done`, `Failed`) + `LastHarvestAt`
  (nullable). Behaviour methods: `MarkHarvest(HarvestStatus, DateTime? at)`. Set `Pending`
  on enqueue, `Running`/`Done`/`Failed` by the queue service. Migration adds the columns.

### 9. Application: quota enforcement

- **`QuotaOptions { int FreeLeadCount = 3 }`** bound from config, registered in Web DI.
- **Ranking.** Unlocked set = the first `FreeLeadCount` of the customer's leads ordered by
  a **stable** key: `Fit` desc (Yes > Maybe > No), then `Score` desc, then `CreatedAt`
  asc, then `Id`. Computed over **all** the customer's leads (any status), so acting on a
  free lead (approve/skip/send) does not promote a locked one — each lead keeps its rank.
- **`Full` plan → nothing is locked.**
- **Enforcement is server-side in the Application layer**, shared by inbox and detail:
  - `GetLeadInbox` returns each `LeadSummary` tagged `Locked` (bool) + the inbox carries
    `LockedCount` and `Plan`.
  - `GetLeadDetail.ForLeadAsync` returns null/refused for a locked lead (so the detail URL
    can't bypass the foil).
  - `LeadWorkflow` approve/send/skip actions refuse a locked lead (defense in depth — a
    locked lead should never be actionable).

### 10. Web: inbox foil UI (`Areas/App/Pages/Index`)

- Unlocked leads render as today.
- **Locked leads** render as real cards with company + draft **blurred** (CSS foil),
  non-clickable, no action buttons.
- A banner / CTA shows the true remaining count: e.g. *"12 more leads ready — Unlock all
  leads"*. The **"Unlock all leads"** CTA opens an upgrade prompt (placeholder page/modal:
  "Upgrade to see every lead — [contact/coming soon]"). No lead detail, no send.
- **Onboarding empty state:** if `HarvestStatus` is `Pending`/`Running` and there are no
  leads yet, show *"Finding your first leads… check back in a few minutes."* If `Failed`
  and no leads, show a graceful *"We couldn't fetch leads yet"* message (no fake leads).
- `Full`-plan tenants see no foil and no upgrade CTA.

## Error Handling

- **Profile generation fails at invite:** log; proceed with invite + email; provision with
  an empty search profile; enqueue harvest anyway — the queue service, finding no
  targeting, marks the customer `Failed` and skips (no bogus leads). Invite never fails
  because of the LLM.
- **Harvest job throws:** caught per-job in the queue service; customer marked `Failed`;
  service continues with the next job.
- **Queue is in-process:** if the Web host restarts with jobs still queued, they are lost.
  Acceptable for this feature (low volume, manual re-harvest possible later). Documented,
  not solved.
- **Locked-lead access via crafted URL:** refused server-side (detail returns not-found /
  workflow refuses), not merely hidden in the UI.

## Testing Strategy

Framework: xUnit + NSubstitute + FluentAssertions; Testcontainers Postgres for infra;
WebApplicationFactory for Web. Strict AAA.

- **Domain:** `Customer.Create` defaults `Plan=Free`, `HarvestStatus=None`; `SetPlan`,
  `MarkHarvest` transitions.
- **Application — quota:** exactly `FreeLeadCount` unlocked; ranking stable when a free
  lead changes status; `Full` plan → zero locked; fewer leads than quota → none locked.
- **Application — profile generation:** request→`SearchProfile` mapping; malformed LLM
  output surfaces as a failure the invite can swallow.
- **Application — enforcement:** `GetLeadDetail` refuses a locked lead; `LeadWorkflow`
  approve/send refuse a locked lead.
- **Infrastructure — queue:** jobs processed sequentially (one at a time); a throwing job
  doesn't stop later jobs; harvest targets the correct tenant with the customer's profile.
- **Web:** invite-from-inquiry prefill; invite sends email + enqueues without blocking on
  harvest; locked-lead detail URL refused; inbox foil renders with correct remaining
  count; onboarding "finding your leads…" state.

## Consequences

**Positive**
- New customers see relevant leads on first login — the core onboarding promise.
- Niche/info the prospect already gave us is finally used.
- Clean, ungameable free-tier boundary with a ready seam for Stripe.
- Harvest becomes triggerable in-process and per-customer, not just a global console run.

**Negative**
- Introduces a background subsystem (queue + hosted service) in the Web host.
- In-process queue loses jobs on restart (accepted).
- Lead relevance still bounded by the fixed sources (accepted, flagged).
- `HarvestLeads` refactor touches a core service; console path must be kept behavior-stable
  (covered by tests).

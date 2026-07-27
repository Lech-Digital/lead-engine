# Recurring Nightly Harvest — Design

**Date:** 2026-07-27
**Status:** Accepted
**Area:** `src/Coldstart.Infrastructure/Harvest`, `src/Coldstart.Application`, `src/Coldstart.Domain` (repo port)

## Problem

The landing page promises *"wake up to your next client, every morning."* Today the per-customer harvest runs only **once** — when an admin invites/provisions a customer (enqueued onto `HarvestQueue`, drained by `HarvestQueueService`) — plus a manual admin re-trigger. There is **no scheduler**, so after the first batch a customer's leads go stale and no fresh leads appear the next morning. The daily loop that the product is sold on does not exist.

## Goal

Add an automated **nightly** harvest that keeps each engaged, paying customer topped up with fresh leads — without rebuilding the "wall" the customer-zone redesign eliminated, and with Groq spend that scales with paying, engaged users rather than total signups.

## Non-goals

- No changes to the harvest pipeline itself (`HarvestLeads.RunForCustomerAsync`), the queue, or the dedup/scoring/enrichment — all reused as-is.
- No per-customer timezone scheduling (single fixed UTC run; the customer base is NL/EU).
- No catch-up for a missed night (if the host is down at run time, that night is skipped).
- No self-serve billing — "Full plan" is set by the existing admin/plan mechanism.
- No admin dashboard for harvest runs (logging only for now; a UI is a possible follow-up).

## Locked decisions (from brainstorming)

1. **Top-up only.** A customer is harvested only when they have room — cost follows engaged users, and the bounded briefing can't refill into a wall.
2. **Threshold: unreviewed < 5.** "Unreviewed" = **unfinished** leads = `Status` in {`PendingReview`, `Approved`} (the same definition the briefing/`LeadBriefing` use). Skip customers with ≥ 5 unfinished.
3. **Full plan only.** `CustomerPlan.Full` customers are harvested nightly; `Free` customers keep their one-time invite sample (nightly leads are the paid benefit).
4. **Cadence: once nightly at ~04:00 UTC.** A single fixed early-morning UTC run so leads are ready before the NL/EU workday. Sequential drain keeps Groq under its rate limit.

## Behaviour

At each `RunAtUtc`:
1. Load the ids of customers **due**: `Plan == Full`, **not** currently `HarvestStatus.Running`, and unfinished-lead count `< TopUpThreshold`.
2. For each due id, `harvestQueue.Enqueue(new HarvestJob(id))`.
3. Log `nightly harvest: enqueued N of M Full-plan customers`.
4. Sleep until the next `RunAtUtc`.

The existing `HarvestQueueService` then drains the queue one job at a time: it resolves targeting via `HarvestTargeting.FromProfile(customer.Profile, customer.Name)`, marks `HarvestStatus` (Running → Done/Failed with timestamp), runs `RunForCustomerAsync`, and dedups by fingerprint. **No change needed there.**

Skipping in-flight (`Running`) customers is a cost guard; fingerprint dedup already makes a double-run harmless to data, but we avoid the wasted LLM spend.

## Components

**New / changed:**

| File | Responsibility |
|---|---|
| `Coldstart.Infrastructure/Harvest/HarvestScheduleOptions.cs` (new) | `Enabled` (bool, default true), `RunAtUtc` (TimeSpan, default `04:00:00`), `TopUpThreshold` (int, default 5). Bound from config section `HarvestSchedule`. |
| `Coldstart.Infrastructure/Harvest/NightlyHarvestScheduler.cs` (new) | `BackgroundService`: computes delay to next `RunAtUtc` from `DateTimeOffset.UtcNow`, wakes, opens a DI scope, calls the due-customers query, enqueues a `HarvestJob` per id, logs, repeats. No-ops when `Enabled == false`. Timer stays thin — all policy lives in the repo query. |
| `Coldstart.Application` port: `ICustomerRepository.IdsDueForHarvestAsync(int threshold, CancellationToken)` (new method) | Returns `IReadOnlyList<Guid>` of Full-plan, not-Running customers whose unfinished-lead count `< threshold`. Single efficient query; **the testable heart of the policy.** |
| `Coldstart.Infrastructure` repo impl (`CustomerRepository`) | Implements the query in EF Core (join/group leads by customer, filter). Returns materialized `List<Guid>` — no `IQueryable` crossing the boundary (CS-DATA-01). |
| `Coldstart.Infrastructure/DependencyInjection.cs` | Register `HarvestScheduleOptions` from config + `AddHostedService<NightlyHarvestScheduler>()` (alongside the existing `HarvestQueueService`). |
| `appsettings.json` | A `HarvestSchedule` section with the defaults (documents the knobs; overridable by env in prod). |

**Reused unchanged:** `HarvestQueue`, `HarvestQueueService`, `IHarvestLeads.RunForCustomerAsync`, `HarvestTargeting.FromProfile`, `Customer.MarkHarvest`, fingerprint dedup.

## Query definition (`IdsDueForHarvestAsync`)

A customer is **due** when all hold:
- `Plan == CustomerPlan.Full`
- `HarvestStatus != Running` (avoid double-enqueue of an in-flight run)
- count of that customer's leads with `Status in (PendingReview, Approved)` `< threshold`

Return their ids. Ordering is unimportant (the queue drains all of them). Must be tenant-safe: this is a system-level cross-customer query (no HTTP tenant context) — it runs with query filters ignored / as an explicit admin-scope read, consistent with how other system paths (e.g. the harvester console) read across customers. The implementer must confirm the existing tenant-filter mechanism and use the same escape hatch the console/seed paths use.

## Testing

- **`IdsDueForHarvestAsync` (integration, real Postgres via Testcontainers):** seed customers spanning the matrix — Full vs Free; unfinished counts 0 / 4 / 5 / 15; one `Running`; one with only Sent/Shelved leads (0 unfinished → due) — and assert the returned set is exactly the Full, not-Running, `< threshold` ones. This is the core policy test.
- **Scheduler enqueue step:** extract the "resolve due ids → enqueue jobs" into a directly-callable method (e.g. `NightlyHarvestScheduler.EnqueueDueAsync(IServiceProvider, CancellationToken)` or a small injected `INightlyHarvestRun`), and test that given seeded customers it enqueues exactly the due ones onto a real/fake `HarvestQueue` (assert the drained/peeked job ids). The `PeriodicTimer`/delay computation itself is not unit-tested (thin, no branching logic worth a test).
- **Disabled flag:** when `Enabled == false`, the hosted service does nothing (no enqueue). A focused test or a documented manual check.
- Follow existing standards: xUnit + FluentAssertions, AAA, `Method_State_Expected`.

## Consequences

**Positive:** the core product promise ("fresh leads every morning") becomes real and automatic; cost is bounded and follows paying, engaged users; the bounded-briefing design is preserved (top-up gate); zero change to the proven harvest/queue pipeline; nightly leads give the paid plan a concrete, sellable benefit.

**Negative:** a missed run (host down at 04:00) is not caught up until the next night; the in-app scheduler couples the nightly job to the web host's uptime (acceptable on a single-server deploy; the sequential queue and idempotent dedup make restarts safe mid-drain); a customer who reviews very fast could clear their <5 and wait until the next night for a refill (by design — the morning ritual, not a live feed).

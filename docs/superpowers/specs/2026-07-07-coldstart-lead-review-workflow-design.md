# Coldstart — Lead Review Workflow (Customer Zone) — Design

**Date:** 2026-07-07
**Status:** Accepted
**Branch:** dotnet-port

## Context

The customer zone (`/app`) currently shows a single flat list of harvested lead
cards (company, FIT, score, source, a `<details>` draft preview). There is no way
to act on a lead: you cannot open one, edit its draft, set a recipient, keep it,
or set it aside. All lead-state transitions exist in the domain (`Approve`,
`Reject`, `Shelve`, `MarkSent`) but are unreachable from the UI.

This feature turns the zone into a working review surface: three tabs
(**New / Accepted / Discarded**), a per-lead detail page for editing and acting,
drag-to-move between tabs, and one-click research links to help find a recipient
address. Email *finding* is limited to research links (a faithful port of the
Python `contact.py`); automated verification and sending are out of scope (see
Non-Goals).

### Relationship to today's earlier change

Earlier on 2026-07-07 `ListLeads` was changed to exclude `Rejected`/`Shelved`
from the inbox (commit 151d251). That was correct for the pre-tabs single list.
**This design supersedes that filter**: the read model returns *all* of a
customer's leads and the page routes each to its tab (Discarded shows
`Shelved` + `Rejected`). The FIT:NO-persisted-as-`Rejected` behavior from the
same commit is retained and relied upon.

## Goals

- Three tabs in `/app`: **New**, **Accepted**, **Discarded**, with sub-groups.
- A per-lead detail page to edit the subject/draft, set a recipient, and act.
- Accept / Discard / Reopen / Mark-as-sent transitions, wired end-to-end.
- Drag a card onto a tab header to move it between states.
- Research links (LinkedIn + Google people-search) per lead to find the buyer.
- Strict per-customer tenant isolation on every read and write.

## Non-Goals (deferred, consistent with the roadmap)

- Actual Gmail/SMTP **sending** (opt-in, later stage).
- Automated email **verification** (MX+RCPT probe / third-party API). Port 25
  egress is blocked on the planned Cloud Run deploy, so SMTP probing is a trap;
  a verifier can slot behind a future port if ever wanted.
- Pagination / search (per-customer volume is small).

## State Model & Tab Mapping

Reuses the existing `LeadStatus` enum (`PendingReview, Approved, Rejected, Sent,
Shelved`) — **no new values**. The key distinction:

- **`Shelved` = the operator discarded it.**
- **`Rejected` = the engine auto-skipped it (FIT:NO).**

| Tab | Statuses | Sub-groups |
|---|---|---|
| **New** | `PendingReview` | — |
| **Accepted** | `Approved`, `Sent` | "Ready" (Approved) · "Sent" (Sent, w/ date) |
| **Discarded** | `Shelved`, `Rejected` | "You discarded" (Shelved) · "Auto-skipped · FIT:NO" (Rejected) |

Accepted leads **without a recipient** are flagged "no recipient yet" in the tab.

### Allowed transitions

```
PendingReview        --Accept--> Approved        (recipient optional)
PendingReview        --Discard--> Shelved
Approved             --MarkSent--> Sent          (requires recipient)
Approved             --Discard--> Shelved
Approved             --Reopen--> PendingReview    (un-accept)
Shelved   / Rejected --Accept--> Approved         (rescue straight to accepted)
Shelved   / Rejected --Reopen--> PendingReview    (rescue an auto-skipped / undo a discard)
Sent                 -> (terminal; read-only, not draggable)
```

`Accept` is reachable from any non-`Sent` state; `Reopen` from any non-`Sent`,
non-`PendingReview` state. Editing subject/draft/recipient is allowed in any
non-`Sent` state.

### Drag semantics

A card dragged onto a tab header maps to exactly one transition, by the **target
tab** (not the sub-group):

| Drop target | Transition | Resulting status |
|---|---|---|
| New | Reopen | `PendingReview` |
| Accepted | Accept | `Approved` |
| Discarded | Discard | `Shelved` |

Rules:
- A drag **never** produces `Rejected` (engine-only) or `Sent` (needs a recipient
  — stays a detail-page action).
- Dropping a card on **its own current tab is a no-op** — determined by tab, so a
  `Rejected` (auto-skipped) card dropped on Discarded does nothing (it is already
  in the Discarded tab), it does not become `Shelved`.
- `Sent` cards are **not draggable** (terminal).

## Domain Changes (`Coldstart.Domain.Lead`)

- `Approve(EmailAddress? recipient = null)` — relax the current
  "recipient required" rule so the recipient is **optional**; still throws if the
  lead is already `Sent`. Sets `Status = Approved` and the recipient when given.
- `SetRecipient(EmailAddress recipient)` — set/replace the recipient; throws if `Sent`.
- `ReviseDraft(string subject, string draft)` — update subject/draft; throws if `Sent`.
- `Discard()` — `Status = Shelved` (operator discard; distinct from `Reject()`).
- `Reopen()` — from `Approved`, `Shelved`, or `Rejected` → `PendingReview`
  (un-accept, or rescue an auto-skipped / undo a discard); throws if `Sent`, no-op
  if already `PendingReview`.
- `Approve` is likewise callable from `Shelved`/`Rejected` (rescue straight to
  Accepted), not only from `PendingReview` — it throws only if `Sent`.
- `MarkSent(DateTimeOffset when)` — **unchanged invariant**: requires `Approved`
  and a non-null recipient.

`Reject()` (engine auto-skip) stays as-is and is not called from the UI.

## Application Changes

Ports live in the Application layer (CS-HEX-02); implementations are `internal`
(CS-MOD-01). All IO is async with `CancellationToken` (CS-DATA-03).

### Repository port (`ILeadRepository`)

Add:
- `Task<Lead?> GetAsync(LeadId id, CancellationToken ct)`
- `Task UpdateAsync(Lead lead, CancellationToken ct)`

### Inbound workflow port (`ILeadWorkflow`)

One cohesive port over five tiny interfaces:

```csharp
public interface ILeadWorkflow
{
    Task<LeadActionResult> SaveDraftAsync(Guid customerId, Guid leadId, string subject, string draft, string? recipient, CancellationToken ct);
    Task<LeadActionResult> AcceptAsync (Guid customerId, Guid leadId, string subject, string draft, string? recipient, CancellationToken ct);
    Task<LeadActionResult> DiscardAsync(Guid customerId, Guid leadId, CancellationToken ct);
    Task<LeadActionResult> ReopenAsync (Guid customerId, Guid leadId, CancellationToken ct);
    Task<LeadActionResult> MarkSentAsync(Guid customerId, Guid leadId, CancellationToken ct);
}
```

`LeadActionResult` is a small result type (e.g. `Ok` / `NotFound` / `Invalid`
with a message) so the Web layer can translate to redirect / 404 / validation
without exceptions for control flow.

**Tenant isolation (critical):** every method loads the lead via
`GetAsync`, and if the lead is null **or** `lead.CustomerId.Value != customerId`
returns `NotFound` and performs **no mutation**. This is the single choke point
for authorization; it is covered by an adversarial test.

Recipient strings are parsed to `EmailAddress` at this boundary; an invalid
non-empty recipient yields `Invalid` (empty/whitespace = no recipient).

### Read models

- `IListLeads.ForCustomerAsync` — return **all** the customer's leads as
  `LeadSummary` (drop the Rejected/Shelved exclusion added in 151d251). The page
  partitions by `Status`. `LeadSummary` already carries `Status`.
- `IGetLeadDetail.ForLeadAsync(Guid customerId, Guid leadId, CancellationToken ct)`
  — returns a `LeadDetail` view (company, fit, score, source, subject, draft,
  recipient, status, sent-at, and computed **contact links**), or `null` if the
  lead is missing or not owned by the customer (same tenant check).

### Contact research links (port of `contact.py`)

`ContactLinkBuilder` — a pure helper (no IO, no persistence), ported faithfully
from `leadengine/contact.py`:

- Input: `company`, a buyer title, and the lead's `sourceUrl`.
- Output: an ordered list of `(label, url)`:
  - `Find buyer (<title>)` → `https://www.linkedin.com/search/results/people/?keywords=<company title>`
  - `Web search` → `https://www.google.com/search?q=<company title LinkedIn>`
  - `Signal source` → the raw `sourceUrl`, when present.
- Returns empty when the company is blank or one of `-`, `unknown`, `n/a`.
- Buyer title: the codebase has no `buyer_titles` config; use a sensible default
  (`"Head of Growth"`). URL-encode query params (`Uri.EscapeDataString`).

Computed inside the `LeadDetail` read model from the stored `Company`/`SourceUrl`.

## Web Layer (`Coldstart.Web`, Area `App`)

### `/app` (Index)

- Query param `?tab=new|accepted|discarded` (default `new`); server-rendered.
- Loads all leads, partitions into the three tabs + sub-groups, renders counts on
  each tab header. Cards link to `/app/leads/{id}`.
- Cards are `draggable="true"`; tab headers are drop zones (see Drag below).
- `[Authorize]`; uses the logged-in user's `CustomerId` for every query.

### `/app/leads/{id}` (Detail)

- Razor Page, `[Authorize]`, tenant-checked. Missing / cross-tenant id →
  redirect to `/app` (no leak).
- Read-only header: company, FIT, score, source link, current status (+ sent date).
- Editable form: subject, draft (textarea), recipient email.
- Research-links panel (from `LeadDetail.ContactLinks`).
- State-dependent POST handlers, each routing through `ILeadWorkflow`:
  `OnPostSave`, `OnPostAccept`, `OnPostDiscard`, `OnPostReopen`, `OnPostMarkSent`.
  `MarkSent` is disabled/blocked when there is no recipient. `Sent` leads render
  read-only.

### Drag-to-move (progressive enhancement)

- `POST /app?handler=Move` (page handler `OnPostMoveAsync(Guid id, string tab, ct)`)
  returns a small `JsonResult` (`{ ok, status, message }`). Antiforgery token sent
  via the `RequestVerificationToken` header (token emitted into the page).
- Maps `tab` → transition: `new`→Reopen, `accepted`→Accept, `discarded`→Discard.
  Unknown tab or a self-move → `Invalid` / no-op.
- Client JS (`wwwroot/js`, in the site's existing plain-JS style): dragstart sets
  the lead id; tab-header dragover/drop triggers the fetch; on `ok` the card
  animates out and the source + target tab counts decrement/increment; on failure
  the card stays and a brief message shows.
- **No-JS fallback:** tabs are ordinary links; all transitions are reachable from
  the detail-page buttons. Drag is enhancement only.

## Error Handling

- Domain guards throw `DomainException` only for true invariant violations
  (e.g. `MarkSent` without recipient, editing a `Sent` lead). The workflow layer
  surfaces expected conditions (not found, invalid email, wrong state) as
  `LeadActionResult`, not exceptions.
- Cross-tenant / missing lead → `NotFound` → Web redirect or 404; never a stack
  trace, never a data leak.
- The `Move` endpoint returns a JSON error payload the client shows inline; the
  card reverts to its original tab.

## Testing (xUnit + NSubstitute + FluentAssertions, strict AAA)

**Domain (`Coldstart.Domain.Tests`):**
- `Approve` without recipient → `Approved`, recipient null (happy).
- `Approve`/edit/`Reopen` on a `Sent` lead → throws (sad).
- `MarkSent` without recipient → throws; with recipient → `Sent` + `SentAt` (edge/happy).
- `Reopen` from `Approved`, `Shelved`, and `Rejected` → `PendingReview`.
- `Approve` from `Shelved`/`Rejected` → `Approved` (rescue).
- `Discard` → `Shelved` (not `Rejected`).

**Application (`Coldstart.Application.Tests`):**
- `ILeadWorkflow` happy paths for each transition (fake `ILeadRepository`), asserting
  the persisted lead's new status and that `UpdateAsync` was called.
- **Adversarial tenant isolation:** calling any workflow method with a mismatched
  `customerId` → `NotFound`, `UpdateAsync` never called.
- Invalid recipient string → `Invalid`, no mutation.
- `ContactLinkBuilder`: correct URLs + encoding; empty for blank/`-`/`unknown`.
- `IGetLeadDetail`: returns null for cross-tenant/missing; maps fields + links.
- `IListLeads`: returns all statuses (no filtering).

**Web (`Coldstart.Web.Tests`, Testcontainers Postgres):**
- Open detail for an owned lead → 200 with subject/draft/links.
- Accept (recipient optional), Discard, Reopen, Mark-sent happy paths persist + redirect.
- Cross-tenant detail GET and cross-tenant `Move`/action → redirect/404, no mutation.
- Tab rendering: a lead of each status appears under the correct tab/sub-group.
- `Move` handler: valid move returns `ok` + new status; self-move / bad tab rejected.

Minimum 1 happy + 2 sad/edge per new class.

## Rollout / Sequencing

Single spec → single plan → subagent-driven build on `dotnet-port`. Suggested task
order: (1) domain transitions, (2) repository `Get`/`Update`, (3) `ILeadWorkflow`
+ tenant isolation, (4) read models + `ContactLinkBuilder`, (5) tabbed Index,
(6) detail page + handlers, (7) drag-to-move endpoint + JS. Each task:
implementer + reviewer, suite green + 0 warnings.

## Consequences

**Positive:** the zone becomes a usable daily review tool; every lead state is
reachable and tenant-safe; research links close the "who do I email" gap without
fabricating addresses or depending on port 25; the design leaves clean seams for
a future verifier and for sending.

**Negative:** adds client-side JS + a JSON endpoint (first non-form POST in the
app) — mitigated by keeping drag as pure enhancement with full no-JS fallback.
Reverting today's inbox filter means the "New" tab, not the read model, is now
responsible for hiding auto-`Rejected` leads.

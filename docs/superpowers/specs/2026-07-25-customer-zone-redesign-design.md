# Customer Zone Redesign — Design

**Date:** 2026-07-25
**Status:** Accepted
**Area:** `src/Coldstart.Web/Areas/App`

## Problem

The customer zone (`/app`) organizes leads into **three tabs** — *To review* / *Approved* / *Skipped* — each an unbounded pile with a count. At real volume (≈20 leads/day → ≈600/month) the "To review" pile becomes a wall no one can face, and the tabs make each pile feel like homework that never ends. The company profile card also dominates the top of the page, pushing the leads down.

## Goals

- Remove the tabs.
- Make the home screen a **bounded, scannable daily briefing** — volume never presents as a wall.
- Keep the brand ritual ("read it over coffee, edit, and send") and the promise that **nothing is sent without the customer, and the prospect never sees Coldstart**.
- No backend rewrite — reuse the existing lead workflow and inbox.

## Non-goals

- No self-serve billing / Stripe (upgrade stays "email us"; the foil + modal are unchanged in intent).
- No auto-send and no lead expiry (an explicit product choice — nothing a customer hasn't reviewed is ever sent or deleted).
- No drag-and-drop (removed; replaced by inline Skip + the focused-review actions).

## Locked decisions (from brainstorming)

1. **Core model — daily briefing queue.** Home shows today's/newest leads as a queue, not tabbed piles.
2. **Volume bound — newest-N cap (N = 15).** The queue shows the newest 15 unreviewed leads. Everything older stays reachable in History and **nothing expires**.
3. **Approve opens the draft to edit & send.** The only inline action on a queue row is **Skip**; pursuing a lead opens the focused review (editor + send).
4. **Screen shape — hybrid list + focus.** Home is a scannable list; clicking a row opens focused review; a **Review all →** button walks the queue one lead at a time.
5. **Free-plan foil — blurred rows inline.** Locked leads appear as blurred rows in the list with a 🔒 Unlock affordance, plus an "Unlock all N leads" CTA and the existing upgrade modal.
6. **Sending — from the customer's own inbox.** The prospect receives a normal email from the customer's real address; Coldstart is invisible. "Send from Coldstart" remains **SuperAdmin-only**.
7. **Profile off the home screen.** The full profile card moves to `/app/profile` (already exists); the briefing header carries a compact company chip linking there.

## Screens & information architecture

Three screens, no tabs:

| Route | Screen | Notes |
|---|---|---|
| `/app` | **Your morning briefing** | The bounded queue (rebuilt) |
| `/app/leads/{id}` | **Focused review** | Existing detail page, restyled + run mode |
| `/app/history` | **History** | New page; replaces the Approved/Skipped tabs |

### 1. Briefing (`/app`)

**Header (app-bar):** wordmark · company chip `Acme BV ▾` (→ `/app/profile`) · Log out. The large profile card is removed from this page.

**Greeting:** `Good morning — N found overnight` (N = leads created since the customer last saw them / in the last ~24h, using the existing "overnight" logic). Sub-stat line: `latest HH:mm UTC · X sent today · Y in history`. A **Review all →** primary button sits beside the greeting (hidden when the queue is empty).

**Queue list:** the **newest 15 unfinished** leads (`PendingReview`, plus any `Approved`-but-not-sent left mid-flow), newest first. Each row:
- status dot (accent = unseen, muted = seen)
- **company** · fit chip (`HIGH` / `MED` via existing `.fit .fit-high|.fit-medium`) · `⚡ <top matched signal>`
- subject snippet (truncated)
- an inline **Skip ✕** button (round, right-aligned)

Row behavior:
- Click the row → focused review (`/app/leads/{id}`).
- Click **Skip ✕** → POST discard (existing `OnPostMove` / `DiscardAsync`), row animates out; no page change.

**Locked (free plan):** locked pending leads render as **blurred rows** inline (company/subject blurred, non-interactive) with a `🔒 Unlock` pill; clicking opens the upgrade modal. Show up to 3 blurred rows to convey what's missing.

**Backlog + foil footer:**
- If more than 15 pending exist: a quiet line `+ M older leads waiting · see them in History →`.
- If `LockedCount > 0`: `Unlock all N leads` CTA → upgrade modal (existing copy: email `andrzej@getcoldstart.nl`, self-serve coming soon).

**States (preserved, restyled):**
- Harvest pending/running & no leads yet → "Finding your first leads… refresh shortly."
- Harvest failed & no leads → "We couldn't fetch leads yet — we'll try again."
- Zero new but history exists → "Good morning — no new leads this morning. The engine runs again tonight." (Queue empty; Review all hidden.)

### 2. Focused review (`/app/leads/{id}`)

The existing detail page, restyled to read as a calm review step. Content:
- Back link → briefing.
- **Run bar** (only in run mode): `Reviewing X of N from this morning` + `Skip → next`.
- Lead header: company · fit chip · `⚡ why` line with the matched signal and a link to find the decision-maker.
- **Subject** (editable input) and **Draft** (editable textarea) — the existing `SaveDraftAsync` fields.
- **Send to** recipient field (existing recipient handling).
- Actions: **Skip** · **Save draft**.
- **No separate "Approve" button.** Sending folds approve in: **Mark as sent** (and the admin-only Send-from-Coldstart) first saves the current subject/draft/recipient and approves the lead (existing `AcceptAsync`), then performs the send/mark transition (`MarkSentAsync` / `SendFromColdstartAsync`, which require an approved lead). "Save draft" persists edits without approving (`SaveDraftAsync`). The plan must confirm the domain permits `MarkSent` on a just-approved lead.
- **Send panel — "Send from your own inbox":** copy reads *"Send it from your own inbox — <Prospect> sees you, never Coldstart."* Buttons: `✉ Open in Gmail` · `✉ Open in email app` · `⧉ Copy email` (existing mailto/gmail/copy handoff), then **Mark as sent ✓** (existing `MarkSentAsync`). In run mode the primary reads **Mark as sent ✓ → next** and advances to the next queue lead; Skip likewise advances.
- **Send from Coldstart** stays gated behind `SuperAdmin` (unchanged).
- Notes panel unchanged.

**Run mode:** entered via `Review all →` (e.g. `?run=1`). It keeps the run bar and turns Skip/Mark-as-sent into "→ next", navigating to the next `PendingReview` lead in the queue order; when the queue is exhausted, return to the briefing with a "You're all caught up" note.

### 3. History (`/app/history`)

One page replacing the two non-review tabs. Filter **chips** (not tabs) with counts + a company search box:
- `All` · `Sent` · `Skipped` · `Older unreviewed`
- `Sent` = `Sent`. `Skipped` = `Shelved` (you) + `Rejected` (auto — "not a fit"), distinguished by badge text. `Older unreviewed` = unfinished leads (`PendingReview` + `Approved`-not-sent) beyond the newest-15 queue.

Rows show company · status badge (`SENT · dd MMM` green / `SKIPPED · you|not a fit` grey / `OLDER · dd MMM` amber) · subject. `Older unreviewed` rows offer **reopen →** (existing `ReopenAsync` / navigates to focused review). Search filters by company name. Nothing expires; this is where the long tail lives, one search away.

## Component / file plan

**Reused as-is (no change):**
- `IGetLeadInbox.ForCustomerAsync` — leads, plan, harvest status, locked count.
- `ILeadWorkflow` — `SaveDraftAsync`, `AcceptAsync`/`ApproveAsync`, `DiscardAsync`, `ReopenAsync`, `MarkSentAsync`, `SendFromColdstartAsync`, notes.
- `IGetCompanyProfile.ForCustomerAsync`.
- Upgrade modal + hint system.

**Changed:**
- `Areas/App/Pages/Index.cshtml(.cs)` — replace tabs/buckets with the briefing queue (newest-15), blurred-inline foil, backlog line, header company chip; remove the profile card block and drag-drop.
- `Areas/App/Pages/Leads/Detail.cshtml(.cs)` — restyle as focused review; add run mode (run bar + "→ next" advance); surface the "Send from your own inbox" panel prominently for all customers.
- `wwwroot/js/app-leads.js` — drop drag-and-drop; keep inline Skip via `fetch` POST; add run-mode "→ next" navigation.
- `wwwroot/css/site.css` — new briefing/history/review styles (reuse existing tokens, buttons, `.fit`, `.wordmark`, `.app-bar`).

**New:**
- `Areas/App/Pages/History.cshtml(.cs)` at `/app/history` — filterable/searchable list; server-side filter by chip + company query.

## Data / status mapping

Existing statuses drive everything; no schema change:
- `PendingReview` (and `Approved`-not-sent) → **unfinished**: newest 15 in the briefing queue, the rest in History "Older unreviewed".
- `Approved` → transient. The review step folds approve into the send action, so Approved is a brief intermediate state, not a browsing bucket. A lead left Approved-but-not-sent (approved then navigated away) is grouped with `PendingReview` as **unfinished** (see above), so it is never orphaned.
- `Sent` → History (Sent).
- `Shelved` → History (Skipped · you).
- `Rejected` → History (Skipped · not a fit).

## Testing approach

- **Briefing:** renders no tabs; shows at most 15 queue rows; shows the backlog line when >15 pending; shows blurred foil rows + Unlock CTA when `LockedCount > 0`; correct greeting for each harvest/empty state.
- **History:** each filter chip returns the right status set; company search narrows results; "Older unreviewed" surfaces pending-beyond-15 and offers reopen.
- **Focused review:** run mode advances to the next queue lead on Skip/Mark-as-sent; "Send from Coldstart" stays hidden for non-SuperAdmin; the send panel names the customer's own inbox.
- Follow existing `Coldstart.Web.Tests` patterns for area pages (authenticated test client).

## Consequences

**Positive:** volume never presents as a wall; one clear daily action; tabs gone; the coffee-ritual "Review all" flow exists; profile out of the way; no backend rewrite; the "prospect never sees Coldstart" promise is explicit in the UI.

**Negative:** a second place to look (History) for anything not in today's queue; the newest-15 cap means a heavy backlog day pushes some pending leads into History immediately (mitigated by the visible backlog line and the "Older unreviewed" filter); drag-and-drop is gone (acceptable — inline Skip + focused review cover the same moves).

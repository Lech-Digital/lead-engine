# Tester feedback round 1 — design

**Date:** 2026-08-05
**Status:** Approved (brainstormed with mockups in `.superpowers/brainstorm/`)

## Context

The first two testing users went through their leads and sent detailed feedback. Verified against the code:

1. **Structural repetition across drafts** — every draft shares one sentence skeleton because `GroqLeadEnricher.BuildPrompt()` hard-codes a single structure (greeting → congrats naming the round → "pressure this moment creates"), temperature 0.4, no variance mechanism. Post-2025 Google spam updates flag structural (not just keyword) repetition across sends, so this quietly threatens deliverability at scale. Highest-leverage fix.
2. **"Closing private credit managers" in an AI-voice-company draft** — not a variable leak ("private credit" appears nowhere in the repo). The prompt instructs the LLM to *invent* the prospect's customer segment ("who do THEY sell to? Invent it from THEIR market"), which is an invitation to hallucinate.
3. **All leads scored 0.50 / "none"** — `SignalScorer` returns `max(keywordScore, baseScore)` with `BaseScore = 0.5`; a funding-only lead with no niche-keyword hits lands exactly on the floor. The UI presents this as if it were a quality verdict.
4. **"Reopen anytime" promise broken** — skip confirmation says "It moves to History under 'Skipped' — you can reopen it anytime", but `History.cshtml` renders the reopen button only for "Older unreviewed" rows; skipped rows aren't clickable at all. Backend (`LeadWorkflow.ReopenAsync`, `Lead.Reopen()`) fully supports reopening.
5. Both panels navigate via pill rows (`_SettingsNav.cshtml`, `_AdminHeader.cshtml`, `.settings-nav` in `site.css`); the customer panel's top level has only scattered app-bar links. Redesign to a proper sidebar (mockup-approved).
6. The tester (customer "Independent") gets the full plan free for 5 days as a thank-you, notified by email — but there is no plan-grant mechanism outside Stripe webhooks (`CustomerPlan { Free, Full }`, `ActivateFull` webhook-only, no expiry concept).

Implementation order: quick wins first — reopen fix → complimentary access → sidebar → draft variance.

## 1. Reopen skipped leads (UI-only bug fix)

- History rows become links to `/app/leads/{id}` for all statuses (Detail already renders every status).
- Skipped rows additionally get the inline "reopen →" form the "older" rows already have, posting to the existing `OnPostReopenAsync` handler.
- No domain/workflow changes. Reopened leads return to `PendingReview` and reappear in the briefing queue.

## 2. Complimentary access

### Domain
- New nullable `ComplimentaryUntil` (DateTimeOffset) on `Customer` (+ EF migration).
- `Customer.GrantComplimentary(DateTimeOffset until)`: sets `Plan = Full` and `ComplimentaryUntil = until`. Throws `DomainException` if a live Stripe subscription exists or `until` is in the past.
- `ActivateFull(...)` (Stripe) clears `ComplimentaryUntil` — a real subscription supersedes the trial.
- `Plan` stays the single source of truth; every existing `Plan == Full` check (lead unlocking, cockpit stats, weekly digest) keeps working untouched.

### Expiry sweep
- A nightly background sweep (hosted service alongside the existing follow-up/retention senders): customers with `ComplimentaryUntil < now && StripeSubscriptionId == null` → `DowngradeToFree(endedAt: null)` and clear `ComplimentaryUntil`.
- `endedAt: null` deliberately — a comp trial never paid, so it must not start the privacy-policy §7 retention-deletion clock.

### Admin API + CLI
- `POST /api/admin/customers/{id}/complimentary` with body `{ "days": n }` → grants until `now + n days`, returns the new state; surfaced as a CLI command.
- Grant triggers the notification email; API responds with grant details either way, but reports whether the email was sent.

### Automated grant email
- Template in `EmailTemplates` (site style): "You've got the full Coldstart plan until {date}" — what unlocks (all leads visible, no queue cap paywall), no card needed, sign-in link. Sent via existing `IEmailSender` (`SmtpEmailSender`) to the customer's account email.
- Separately (not code): a personal reply draft for the operator to send from the admin Mailbox — thanking the tester and honestly answering the scoring question.

## 3. Scoring honesty (small UX fix)

On the lead detail page, when `score == baseScore` and no signals matched, display `0.50 · baseline — source signal only` instead of implying a quality tier. No scoring-logic change.

## 4. Sidebar shell (replaces pill navigation)

Approved mockup: full-height dark rail, wordmark on top, active item marked with an amber left edge, `Log out` as the bottom item in muted red, opening a confirmation dialog ("Log out? / Stay / Log out") that reuses the existing `_ConfirmDialog` component (partial moves somewhere both areas can reach, e.g. `/Pages/Shared/`).

- **Customer rail** (whole app area — briefing, history, lead detail, settings): Briefing · History · SETTINGS group (Company, Outreach, Sources, Notifications, Subscription, Account) · Log out. Replaces `_SettingsNav` pills and the scattered app-bar links. Subscription entry keeps its current visibility rule (Full plan only).
- **Admin rail**: Cockpit · Companies & invites · Mailbox · Exclusions · EXTERNAL group (Visitors ↗, Stripe ↗) · Log out. Replaces `_AdminHeader` pills.
- One sidebar partial per area (`_AppSidebar.cshtml`, `_AdminSidebar.cshtml`) sharing the same CSS, rendered via an app-shell layout so pages stop pasting their own `app-bar` headers.
- **Mobile**: rail collapses behind a hamburger in a slim top bar (CSS + minimal vanilla JS).
- Old `.settings-nav` pill CSS and scroll-restore scripts retire with the pills.

## 5. Draft variance (generation-level)

All changes in `GroqLeadEnricher` / the harvest pipeline.

### Skeleton rotation
- 5 distinct draft skeletons for normal mode (observation-first, question-first, pressure-first, ultra-short two-liner, outcome-first) and 3 opener/analysis variants for fixed-pitch mode.
- 3–4 subject-line patterns replacing the single hard-coded "«moment» + an idea for «company»" shape (subjects are the first thing filters compare across sends).
- Variant selected by a stable hash of the lead's identity (link/company) → deterministic per lead, spread across a night's batch, unit-testable. Shared invariants (greeting rules, honesty rules, banned-phrase list) stay common to all skeletons.

### Hallucination guardrail
- Replace "Invent it from THEIR market" with: name specific decision-makers/segments **only when evidenced in the harvested content**; otherwise stay general. Kills the "private credit managers" class of error.

### Second data point
- New Application port `ISecondaryContextLookup`; Infrastructure implementation on the existing free LangSearch source.
- Before enrichment, fetch one snippet about the company (own announcement / founder quote) and pass it into the prompt as a SECONDARY CONTEXT block, usable only as additional evidence under the same no-invention rules.
- Config-gated (off switch) in case it slows batches or the free tier runs dry; enrichment proceeds without it when lookup fails or returns nothing.

## Error handling

- Grant API: 404 unknown customer, 400 invalid days / already-subscribed (domain guard message).
- Email failure on grant: grant still stands; failure logged and reported in the API response.
- Secondary lookup: any failure → enrich without secondary context (never blocks the batch).
- Expiry sweep: per-customer try/catch so one bad row can't stop the sweep (same pattern as existing senders).

## Testing

xUnit + NSubstitute + FluentAssertions, AAA, `MethodName_State_Expected`; ≥1 happy + 2 sad paths per new class:

- Domain: `GrantComplimentary` (happy, past date, live Stripe sub), `ActivateFull` clears `ComplimentaryUntil`, expiry semantics.
- Sweep service: downgrades lapsed comp customers, skips Stripe subscribers, isolates per-row failures, leaves retention clock unset.
- Rotation: same lead → same skeleton; a batch of distinct leads spans multiple skeletons and subject patterns.
- Prompt content: guardrail text present, secondary context included/excluded correctly.
- History page model / view: skipped rows expose reopen; rows link to detail.
- Grant endpoint: auth, happy path, unknown customer, email-failure reporting.

## Rollout

1. Land in order: reopen fix → complimentary (+ migration) → sidebar → variance.
2. Deploy to Hetzner prod (existing compose flow), run migration.
3. Confirm the tester's customer record + email via the prod admin API, then grant 5 days via the new CLI command (sends the automated email).
4. Operator sends the personal feedback reply from the admin Mailbox.

## Consequences

- **Positive:** honest UX (reopen, scoring), reusable trial mechanism for future testers, structurally diverse drafts that protect deliverability, a navigation shell that scales with new sections.
- **Negative:** sidebar touches every panel page (visual-regression risk on mobile widths); secondary lookup adds per-lead latency and a soft dependency on LangSearch's free tier (mitigated by the off switch).

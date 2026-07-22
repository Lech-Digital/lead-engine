# Lead review redesign — compact tabs, elaborate detail, notes, send handoff

**Date:** 2026-07-22
**Status:** Approved by Andrzej (brainstorming session; card option C + detail option A)

## Problem

The customer-zone review surface is too thin and clumsy: tab cards don't show enough to judge a lead without opening it; the opened lead is a flat stack of editable fields with no company context or read→decide→send flow; and "Mark as sent" is broken in practice — it renders on every not-yet-sent lead but the domain only allows it on an **Approved** lead **with a recipient**, so on a "To review" lead it fails with "Only an approved lead with a recipient can be marked sent." There is also no way to keep notes on a lead, and no clear path to actually get the drafted email out.

## Product decision on sending

Coldstart drafts; the customer sends **from their own domain** (sending on their behalf from `@getcoldstart.nl` is unprofessional and harms deliverability/reply routing). Per-mailbox integration (Gmail/Microsoft OAuth send) is deferred as a future premium option. For now the detail view makes the draft **trivially usable in the customer's own mail client** via three handoffs, after which they record it with "Mark as sent". No automated sending is built.

## Scope

Presentation + a small domain addition (lead notes). Files: customer-zone `/app` list (`Areas/App/Pages/Index.cshtml[.cs]`), lead detail (`Areas/App/Pages/Leads/Detail.cshtml[.cs]`), `site.css`, a new detail JS for the send handoffs; Application `LeadWorkflow`/`LeadDetail`/`IGetLeadDetail`; Domain `Lead` + new `LeadNote`; one EF migration. Reuses existing fields (`MatchedSignals`, `SourceUrl`, `Score`, `EmailConfidence`, `ContactLinkBuilder`).

## 1. The list — compact rows (card option C)

Each tab (To review / Approved / Skipped — unchanged slugs/routes) renders leads as one-line rows instead of large cards:

- Line 1: company (display font) + fit badge (YES green / MAYBE amber / NO muted) on the left; mono timestamp (`dd MMM · HH:mm`) on the right.
- Line 2: `⚡ {why-now}` · `{subject}` in muted text, truncated with ellipsis (why-now = first matched signal / `MatchedSignals`, already on `LeadSummary`).
- Whole row links to the detail page. Approve/Skip buttons appear on row **hover** (and are always present for keyboard/no-hover via focus-within); drag-to-tab move is retained (same `data-lead-id`/`data-bucket`, same `?handler=Move` endpoint and `app-leads.js`).
- The Approved tab keeps its Ready/Sent groups; Skipped keeps You-skipped / Auto-skipped. Empty states unchanged in wording.

No new data for the list — `LeadSummary` already carries Company/Fit/Subject/Source/Score/CreatedAt/MatchedSignals.

## 2. The opened lead — two columns (detail option A)

`Detail.cshtml` becomes a two-column layout (stacks to one column under ~900px). Header: company (display) + fit badge + a status line (`To review · found HH:mm` / `Approved` / `Sent on yyyy-MM-dd` / `Skipped`), and a "← Back to your briefing" link.

**Left column — the email (the main job):**
- Editable `Recipient` (email), `Subject`, `Body` (textarea). "Set a recipient before sending" placeholder when empty.
- **Status-driven actions:**
  - *PendingReview:* `Save draft`, `Approve →` (moves to Approved; recipient optional here — harvester never fabricates addresses), `Skip`.
  - *Approved:* recipient/subject/body stay editable with `Save draft`; a **"Send from your own inbox"** panel (below) becomes primary; plus `Mark as sent ✓`, `Move back to review`, `Skip`.
  - *Sent:* read-only draft, shows `SentAt`; only notes remain editable.
  - *Shelved/Rejected:* draft shown read-mostly; `Move back to review`.
- **Send-handoff panel** (shown on Approved leads): three buttons built client-side from the *current* recipient/subject/body field values, so edits are included:
  - **Open in Gmail** → `https://mail.google.com/mail/?view=cm&fs=1&to={to}&su={subject}&body={body}` (URL-encoded), new tab.
  - **Open in email app** → `mailto:{to}?subject={subject}&body={body}`.
  - **Copy email** → `navigator.clipboard.writeText(subject + "\n\n" + body)`, with a "Copied" confirmation.
  All open/compose in the customer's own client → the email is sent from *their* domain. A one-line note states this.
- **Mark as sent** is enabled only when a recipient is present; when absent it's disabled with a hint "Add a recipient first." This fixes the current bug (the button no longer appears on states where it can't work).

**Right column — context + notes:**
- **Why this lead:** `⚡ Why now` (MatchedSignals, humanized), the raw matched signals (mono), Source + a link to `SourceUrl`, Score, EmailConfidence.
- **Find the decision-maker:** the existing `ContactLinkBuilder` links (LinkedIn / Google people-search / signal source) — no fabricated addresses.
- **Notes:** a list of the lead's notes (newest first), each showing text + mono "saved {date}" with **edit** and **delete**; plus an **add-a-note** box. Notes are private to the customer and editable in any status.

## 3. Domain: lead notes (new)

`Lead` gains an owned collection of notes (part of the aggregate — notes exist only through a lead):

```csharp
public sealed class LeadNote        // Domain, in the Lead aggregate
{
    public Guid Id { get; private set; }
    public string Text { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    private LeadNote() { }           // EF
    internal LeadNote(Guid id, string text, DateTimeOffset when) { … }
    internal void Update(string text, DateTimeOffset when) { Text = …; UpdatedAt = when; }
}
```

`Lead` exposes: `IReadOnlyList<LeadNote> Notes` (backed by a private list), `AddNote(string text, DateTimeOffset when) → Guid` (rejects empty/whitespace text via `DomainException`), `UpdateNote(Guid noteId, string text, DateTimeOffset when)` (rejects empty text; a noteId not in the collection is a no-op — idempotent, so a stale tab can't error), `RemoveNote(Guid noteId)` (unknown id is a no-op). Notes are allowed in every status (annotating a Sent lead is fine). Guid ids are supplied by the caller (Web) to keep the domain clock/id-free, consistent with the codebase's "pass time/ids in" pattern.

**EF:** map notes as an owned collection in `LeadConfiguration` — `builder.OwnsMany(l => l.Notes, n => { n.ToTable("lead_notes"); n.WithOwner().HasForeignKey("LeadId"); n.HasKey("Id"); n.Property(x => x.Text).IsRequired(); … });`. Owned collections load/save with the aggregate (no explicit Include). One migration `AddLeadNotes` (new `lead_notes` table: Id, LeadId FK, Text, CreatedAt, UpdatedAt).

## 4. Application

- `LeadDetail` read model gains `IReadOnlyList<LeadNoteView> Notes` (`LeadNoteView(Guid Id, string Text, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt)`) plus `MatchedSignals`, `EmailConfidence` (already on the entity) so the context panel needn't reach past the read model. `GetLeadDetail` maps `lead.Notes`.
- `ILeadWorkflow` gains, all routed through the existing tenant-isolation `MutateAsync` choke point: `AddNoteAsync(customerId, leadId, noteId, text, ct)`, `UpdateNoteAsync(customerId, leadId, noteId, text, ct)`, `RemoveNoteAsync(customerId, leadId, noteId, ct)`. Empty/whitespace note text → `LeadActionResult.Invalid`.
- No change to `MarkSentAsync` (already requires recipient) — the fix is purely that the UI only offers it when valid.

## 5. Web

`Detail.cshtml.cs` gains `OnPostAddNoteAsync`/`OnPostUpdateNoteAsync`/`OnPostRemoveNoteAsync` (PRG-redirect back to the lead; the Web layer generates the note Guid). The send handoffs are pure client-side JS (`wwwroot/js/lead-detail.js`) reading the form fields — no new endpoint. All handlers stay `[Authorize]` + antiforgery.

## Testing

- **Domain** (`LeadTests`): AddNote returns an id and appears in Notes; empty text throws; UpdateNote changes text + stamps UpdatedAt; RemoveNote removes; notes allowed on a Sent lead; unknown noteId on Update/Remove is a no-op (does not throw, collection unchanged).
- **Application** (`LeadWorkflowTests` / detail): add/update/remove note round-trip through `MutateAsync` respects tenant isolation (another customer's lead → NotFound); empty text → Invalid; `LeadDetail` includes notes.
- **Infrastructure** (`LeadRepositoryTests`, real Postgres): a lead with notes round-trips (owned collection persists + reloads); the `lead_notes` migration applies via the Testcontainer.
- **Web** (`LeadDetailTests` / `LeadReviewTests`): compact rows render company+fit+why-now+subject; hover actions + drag move still work; detail two-column renders context + notes; add/edit/delete note via POST persists and is tenant-isolated; Mark-as-sent is absent/disabled on a PendingReview lead and present on an Approved lead with a recipient; the handoff buttons render on Approved leads with correct `mail.google.com`/`mailto:` hrefs (or data attributes the JS reads).
- Existing suite stays green.

## Out of scope (YAGNI)

Keyboard shortcuts / auto-advance to the next lead; integrated/automated sending; bulk actions; note formatting/attachments/@mentions; changing the tab set or move endpoint.

## Consequences

**Positive:** leads are scannable at a glance and fully actionable inside; the "Mark as sent" bug is gone; the send story is honest and professional (customer's own domain) with near-zero build; notes add real CRM value; the domain gains a clean owned-collection aggregate member. **Negative / accepted:** the compact list shows less per row (mitigated — the detail view is one click away and richer); `mailto:`/Gmail links have length limits for very long drafts (acceptable for short outreach; Copy covers the rest); notes are a new table + migration; owned-collection loading adds a small join to lead reads.

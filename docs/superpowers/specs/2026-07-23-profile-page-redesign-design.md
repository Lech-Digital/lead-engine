# Company details page redesign

**Date:** 2026-07-23
**Status:** Approved by Andrzej (brainstorming session)

## Problem

`/app/profile` uses the narrow `auth-shell`/`auth-card` shell (built for login/register), so it's cramped, and its only navigation is a "← Back to your leads" link stranded at the bottom — inconsistent with every lead page, which has a top `app-bar` (wordmark + nav). Make it wider, consistent, and organized.

## Decision

Move the page onto the app-zone layout and group its ~8 fields into titled section-cards, two-column on wide screens (brainstormed option: grouped sections, 2-column). Presentation only — no field or behavior changes.

## Design

- **Shell:** replace `auth-shell`/`auth-card` with the app-zone shell used by `/app` and the lead detail: a top `app-bar` (`coldstart*` wordmark left; nav right: "Your briefing" → `/app`, and the Log out form), then `<main class="wrap app-zone">`. Drop the bottom "← back" link (nav is at the top now).
- **One `<form method="post">`** (keep the hidden first-submit so Enter still saves) containing three Morning-Engine `.panel` section-cards:
  1. **Company** — Company name, Website, Niche, Contact email.
  2. **Registration** — Address (full width), VAT number, KvK number.
  3. **Outreach** — Company brief textarea (full width) + "Polish with AI" button; the "Mention leads were found via Coldstart" checkbox.
  Short fields sit two-per-row via a responsive grid (`.profile-grid`, `grid-template-columns: 1fr 1fr`, collapsing to one column under ~640px); the brief and address span full width.
- **Action row** after the cards: primary **Save** button with the `StatusMessage` rendered inline next to it (not at the page bottom).
- **Onboarding hints** ("Show hints again") in its own small `.panel` after the form, visually separated.
- **CSS:** a new `.profile-*` layer (grid + section spacing) reusing existing tokens/`.panel`/`.field`/`.btn`; the profile page no longer uses `auth-card` (auth pages keep it).

## Behavior (unchanged)

Same fields, same handlers (`OnPostAsync` save, `OnPostPolishAsync`, `OnPostShowHintsAsync`), same `[StringLength(2000)]` on Brief, same hidden-default-submit Enter-saves behavior, same antiforgery. Only markup + CSS change; `Profile.cshtml.cs` needs no logic change (may only adjust if a field id/name must stay stable — it must).

## Testing

Existing `CustomerZoneTests` profile tests (persist brief/toggle, polish preview no-persist, Enter-key hidden default submit precedes Polish, hints reset, save round-trip) assert field names/handlers/markers — they must stay green unmodified. Add one assertion that `/app/profile` renders `app-bar` (shell consistency). Field `id`/`name` attributes (`Input.CompanyName`, `Input.Brief`, `Input.MentionSourceTool`, etc.) and handler names are unchanged.

## Out of scope

No field add/remove; no behavior/validation change; no changes to other pages' `auth-card` use; no new data.

## Consequences

**Positive:** consistent with the rest of the customer zone; wider, scannable, grouped; nav where users expect it. **Negative / accepted:** a `.profile-*` CSS layer is added; the page stops sharing the `auth-card` styling (intentional).

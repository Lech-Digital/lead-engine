# Customer-zone onboarding hints — per-user, server-side

**Date:** 2026-07-22
**Status:** Approved by Andrzej (brainstorming session)

## Problem

The customer zone (`/app`) shows a single explainer strip ("How this works…") whose "Got it" button appears dead: `site.css` gives `.app-explainer { display: flex }`, which overrides the user-agent `[hidden] { display: none }`, so the JS setting `hidden = true` never visually hides the box. The dismissal is also `localStorage`-based (per-browser, one flag, no rotation). Andrzej wants proper onboarding: a finite, ordered set of hints shown one per day, dismissable, tracked per user (not per browser), that runs out and then goes silent — plus a way to bring the hints back.

## Decision

A small per-user onboarding-hint system. Purpose is **onboarding-then-stop** (not a permanent rotating-tips engine). Cadence: **one hint per calendar day; "Got it" retires a hint permanently; the next waits until tomorrow.** State lives **server-side on the user** (approach A), so it follows the operator across devices and survives cache clears. Hint content lives **in code** (a handful of messages — no CMS at this scale).

## Architecture (hexagonal)

Hint state sits on `ApplicationUser` (Infrastructure/Identity). To keep the Application layer dependency-free (CS-HEX-01), the **selection logic is a pure component** operating on plain inputs; the **Web layer** reads/writes the user fields via the `UserManager<ApplicationUser>` it already injects.

### Content — `OnboardingHints` (Application, pure)

`src/Coldstart.Application/Hints/OnboardingHints.cs`: a static ordered list of `Hint(string Key, string Body)` records. Starter set (order = display order), keys stable forever:

1. `how-it-works` — "We find leads overnight → you review and approve → the email goes out. Nothing is ever sent without you."
2. `review-tabs` — "To review, Approved, Skipped — drag a lead between tabs, or open one to edit the draft before approving."
3. `draft-first` — "Every lead comes with a ready first email. Read it, tweak it, approve — or skip and it's gone."
4. `why-now` — "The 'Why now' line is the buying signal we spotted. Lead with it — that's what makes the timing work."

`public sealed record Hint(string Key, string Body);` and `public static IReadOnlyList<Hint> All { get; }`.

### Selection — `HintPicker` (Application, pure, no I/O)

`src/Coldstart.Application/Hints/HintPicker.cs`. Inputs are plain values so it needs no dependency on Identity/EF:

```csharp
public sealed record HintState(IReadOnlyCollection<string> SeenKeys, DateOnly? ShownOn);
public sealed record HintDecision(Hint? ToShow, HintState NextState);  // ToShow null = render nothing

public static class HintPicker
{
    // today passed in (no ambient clock — keeps it pure/testable, mirrors the codebase's DateTimeOffset-in pattern)
    public static HintDecision Pick(HintState state, DateOnly today);
    public static HintState Dismiss(HintState state, string key);      // adds key to Seen
    public static HintState Reset();                                    // empty seen, null ShownOn
}
```

`Pick` rules (exactly one hint per calendar day):
- `candidates` = `OnboardingHints.All` where `Key` not in `SeenKeys`, in order.
- If `candidates` empty → `ToShow = null`, state unchanged.
- Else `next = candidates[0]`.
  - If `ShownOn == today` → show `next` (it's today's active, still-undismissed hint), state unchanged.
  - Else if `ShownOn` is null or `< today` → promote: `ToShow = next`, `NextState.ShownOn = today`. (Advances at most one hint per day.)
- `Dismiss` adds the key to `SeenKeys` (leaves `ShownOn` — so nothing new appears until tomorrow even after dismissing today's).

### State — `ApplicationUser` (Infrastructure)

Add two properties: `public string? HintsSeenCsv { get; set; }` and `public DateOnly? HintShownOn { get; set; }`. One EF migration (`AddUserHintState`). Web maps between `HintsSeenCsv` (comma-split, ignore blanks) and `HintState.SeenKeys`. CSV chosen over a table: it's 1:1 with the user, a handful of short keys, never queried by value.

### Web wiring

- **`/app` (`Areas/App/Pages/Index.cshtml[.cs]`):** in `OnGetAsync`, after loading the user, build `HintState` from the user's fields, call `HintPicker.Pick(state, DateOnly.FromDateTime(DateTime.UtcNow))`; expose `ActiveHint` (the `Hint?`) to the view. If `Pick` promoted a hint (state changed), persist the new `HintShownOn` via `UserManager.UpdateAsync`. Render the explainer strip only when `ActiveHint is not null`, with the hint `Key` in a hidden field.
- **Dismiss handler** `OnPostDismissHintAsync(string key, CancellationToken ct)`: load user, `HintPicker.Dismiss`, write `HintsSeenCsv`, `UpdateAsync`, redirect to `/app` (PRG). The strip's "Got it" becomes a real `<form method="post" asp-page-handler="DismissHint">` with the key + antiforgery — replaces the localStorage JS entirely (that block is removed from `app-leads.js`).
- **Restore control** on `/app/profile` (`Areas/App/Pages/Profile.cshtml[.cs]`): an "Onboarding hints" row with a **"Show hints again"** button → `OnPostShowHintsAsync`: `HintPicker.Reset()`, clear `HintsSeenCsv` + `HintShownOn`, `UpdateAsync`, redirect to `/app/profile` with a confirmation flash.
- **CSS fix:** add `.app-explainer[hidden] { display: none; }` to `site.css` so a dismissed/absent strip is actually hidden (defensive even though the server now controls rendering).

## Testing

- **`HintPickerTests`** (Application, pure, no fixtures — fast): first-visit picks hint #1 and sets `ShownOn`; same-day re-visit keeps showing the undismissed hint without advancing; dismiss then same-day → `Pick` returns null (nothing new today); next day after a dismiss → next hint; all-seen → null; `Reset` → back to #1; unknown/stale seen keys ignored gracefully.
- **Web** (`Coldstart.Web.Tests`, real Postgres factory + `TestAuth`): `/app` first load renders the strip with hint #1 text and a dismiss form carrying `__RequestVerificationToken`; POST `DismissHint` persists and the strip disappears on reload (same day, no second hint); `/app/profile` "Show hints again" restores the strip; dismiss is tenant/user-isolated (another user unaffected). One assertion that `.app-explainer[hidden]` resolves to `display:none` (guard against the regression this fixes).

## Out of scope (YAGNI)

Admin-editable/CMS hint content; ongoing/rotating engagement tips after onboarding; view analytics; multiple hints per day; scheduling beyond one-per-day; localisation.

## Consequences

**Positive:** dismiss actually works; onboarding is paced and finite; state is per-user and portable across devices; logic is a pure, thoroughly-testable unit with no I/O; content edits are a one-line code change; restoring hints is trivial. **Negative / accepted:** hint content requires a deploy to change (fine at this scale); CSV state is denormalised (acceptable — tiny, 1:1, never queried by value); "one per calendar day" uses UTC, so the day boundary isn't the operator's local midnight (acceptable for onboarding pacing).

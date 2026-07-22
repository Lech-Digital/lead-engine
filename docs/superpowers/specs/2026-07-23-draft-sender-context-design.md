# Improved mail drafting — sender company brief + dogfooding angle

**Date:** 2026-07-23
**Status:** Approved by Andrzej (brainstorming session)

## Problem

Drafts are written overnight by `GroqLeadEnricher` from a single global offer config (`NicheOptions.Offer`); they never reference *who the sender is*, so cold emails lack credible "here's who's reaching out" context. The per-customer `CompanyProfile` (address/VAT/niche/…) exists but is not wired into drafting. Two improvements:

1. **Every customer:** a short brief about the sender's own company — admin-entered, AI-polished — that gives each draft credible sender context.
2. **Operator (Andrzej) only:** when selling Coldstart itself, the draft should say it *found the lead using Coldstart* — the exact tool being pitched — as living proof it works. Controlled by a per-account toggle (off by default).

Both shape **newly harvested drafts** on the next run — drafts are generated at harvest time, so existing leads are not retroactively rewritten.

## Decisions (from brainstorming)

- The brief is about the **sender's own company** (additive context alongside the offer), not research about the lead.
- **Polish-on-save, admin-reviewed:** a "Polish with AI" button returns a tightened brief into the editor; the admin reviews/edits and saves. The stored brief is human-approved; no re-polishing at draft time.
- The dogfooding angle is a **per-account boolean toggle**, on for the operator's own account, off by default for everyone.

## Data model

`CompanyProfile` (domain record on `Customer`, currently 7 positional fields) gains two, appended at the end:
- `string? Brief`
- `bool MentionSourceTool`

`CompanyProfile.Empty` updated (`Brief = null`, `MentionSourceTool = false`). EF: `CustomerConfiguration`'s owned `Profile` mapping gains columns `ProfileBrief` (text, nullable) and `ProfileMentionSourceTool` (bool, default false); one migration `AddCompanyBrief`. Positional-record change → update every construction site (`CompanyProfileView`, `UpdateCompanyProfileRequest`, mappers, tests) — `grep -rn "new CompanyProfile(" src tests`.

## Application

- `CompanyProfileView` and `UpdateCompanyProfileRequest` (Application/Customers) gain `Brief` (string?) and `MentionSourceTool` (bool); `IsEmpty` unchanged (brief/toggle don't count toward "empty profile" prompt). `IGetCompanyProfile`/`IUpdateCompanyProfile` mappings carry the two fields through.
- New port `ICompanyBriefPolisher`:
  ```csharp
  public interface ICompanyBriefPolisher
  {
      // Returns the polished brief, or null if polishing is unavailable/failed (caller keeps the raw text).
      Task<string?> PolishAsync(string rawBrief, CancellationToken ct);
  }
  ```
- New per-run sender context passed into enrichment (per-customer, not global config):
  ```csharp
  public sealed record SenderContext(string? Brief, bool MentionSourceTool)
  {
      public static SenderContext None { get; } = new(null, false);
  }
  ```
- `ILeadEnricher.EnrichAsync` gains a `SenderContext sender` parameter (before `CancellationToken`). Update all implementations and call sites.
- `HarvestLeads`: it already resolves the target `Customer` before the enrich loop. Build `SenderContext` from `target.Profile.Brief` + `target.Profile.MentionSourceTool` once, and pass it into every `enricher.EnrichAsync(...)` call.

## Infrastructure

- `GroqLeadEnricher.BuildPrompt` gains the `SenderContext`:
  - When `Brief` is non-empty, add a **`WHO IS REACHING OUT`** section with the brief, and instruct the model to open credibly from the sender's perspective (still leading with a specific detail about the *lead*, per the existing rules).
  - When `MentionSourceTool` is true, append one instruction: *"Mention that you discovered this company using Coldstart — the exact lead-generation tool you are offering them — as concrete proof it works. Keep it to one natural sentence, not a boast."* When false, nothing about Coldstart is added.
- `GroqBriefPolisher : ICompanyBriefPolisher` — a Groq call (reuse the existing `LlmOptions` + HttpClient pattern from `GroqLeadEnricher`, incl. the CRL/backoff handling and best-effort failure → returns `null`). System prompt: tighten the admin's raw text into a 2–3 sentence third-person company brief suitable as email context; never invent facts; return only the brief. DI: `services.AddScoped<ICompanyBriefPolisher, GroqBriefPolisher>()`.

## Web

`/app/profile` (`Areas/App/Pages/Profile.cshtml[.cs]`):
- A **Company brief** textarea (`Input.Brief`) and a **"Mention leads were found via Coldstart"** checkbox (`Input.MentionSourceTool`), both persisted by the existing profile Save (`OnPostAsync`).
- A **"Polish with AI"** button → new `OnPostPolishAsync` handler: calls `ICompanyBriefPolisher.PolishAsync(Input.Brief)`; on success replaces `Input.Brief` with the polished text and re-renders the page (NOT saved yet — admin reviews then clicks Save); on null/failure sets a notice ("Couldn't polish just now — your text is unchanged") and re-renders. `[Authorize]` + antiforgery; the button is a submit posting to the Polish handler.
- The brief/toggle render for any authenticated customer admin; the checkbox is plain UI (no special gating — the operator simply turns it on for their account).

## Testing

- **Domain:** `CompanyProfile` with `Brief`/`MentionSourceTool` round-trips; `Empty` has null brief + false toggle.
- **Infrastructure:**
  - `GroqBriefPolisher`: happy path returns polished text; missing key / transient failure → returns `null` (best-effort), mirroring the enricher's test seams.
  - `GroqLeadEnricher`: capture the outgoing request body via the existing fake `HttpMessageHandler` and assert the prompt **contains the brief text** when a brief is supplied, and **contains the Coldstart/source instruction only when `MentionSourceTool` is true** (and omits it when false).
- **Application:** `HarvestLeads` builds `SenderContext` from the target customer's profile and passes it to the enricher (fake enricher captures the `SenderContext`); a customer with no brief → `SenderContext.None`-equivalent (null brief, false toggle).
- **Web:** profile page renders the brief textarea + toggle; `OnPostPolishAsync` puts polished text into the field without persisting; Save persists both fields; a customer's saved brief round-trips through `/app/profile`.
- Existing suite stays green (the positional `CompanyProfile`/view/request changes ripple through construction sites + the `EnrichAsync` signature through its call sites — fix all).

## Out of scope (YAGNI)

Per-lead custom briefs; polishing anything other than the brief; multiple briefs/personas; retroactively re-drafting existing leads; making the harvester multi-customer in one run (it stays single-target-per-run — the brief is read from that target).

## Consequences

**Positive:** drafts gain credible sender context, materially improving cold-email quality; the operator's Coldstart pitch becomes self-proving; the brief is human-approved (one polish call, cached on the profile); the meta angle is a clean, testable per-account flag rather than hardcoded identity. **Negative / accepted:** shaping drafts requires a fresh harvest run (no retroactive rewrite); the harvester adds a profile read per run; `CompanyProfile`/`EnrichAsync` positional changes touch several call sites; a second Groq-backed adapter (`GroqBriefPolisher`) is added (kept parallel to `GroqLeadEnricher`, not shared, to keep each focused).

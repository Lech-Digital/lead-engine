# Structured targeting profile — design

**Date:** 2026-08-07
**Status:** Approved (sub-project A of three)

## Context

Lead quality is bounded by how well the engine understands who a customer sells to. Today that understanding is a flat keyword bag on `CompanyProfile`: `Offer`, `TargetKeywords`, `BuyingSignals`, `Disqualifiers` — newline-joined strings produced by one LLM pass over two free-text fields (`Niche`, `Info`) at invite time (`GroqSearchProfileGenerator`, called from `AdminUserService.InviteAsync`).

That single bag then does triple duty:

1. **Query generation** — `KeywordRotation` picks keywords to send to the sources as search terms.
2. **Cheap scoring** — `SignalScorer` substring-matches keywords against harvested title+text.
3. **Draft prompting** — the keyword list is handed to `GroqLeadEnricher` as "who is a good fit".

It serves all three badly, which shows up as three distinct failures the operator confirmed seeing: wrong companies surface (precision), obvious good-fit companies never appear (recall), and genuine fits arrive with no reason to contact them today (timing).

The root cause is shared: a flat list has no dimensions, so nothing downstream can be selective. This spec replaces it with a structured profile. Two follow-on sub-projects consume that structure:

- **B — Query generation**: compose many targeted queries from the structure across the free source budget, using the jobs feed as a targeting instrument rather than a keyword feed.
- **C — Qualification**: veto wrong-fit companies in the cheap gate, and judge fit against explicit criteria in the LLM gate.

**A on its own does not improve lead quality.** It is the foundation B and C need, plus the authoring surface the operator and customers use. That is stated plainly so nobody expects better leads the day this ships.

### Constraint that shaped the plan

Recall is capped by money, not modelling. Scrapingdog's free tier is 200 credits/month at 5 credits per search — about 40 searches/month, hence `WebSearch.MaxQueries = 1` (one paid query per night). LangSearch allows 6 free queries; Hacker News, the funding RSS feeds and Adzuna jobs are free. The operator chose to work the free sources harder before paying for search, so B must generate queries that fit inside that budget rather than assuming more of it.

## The model

A new `Targeting` value object, owned by `Customer`, held separately from `CompanyProfile`.

Keeping it separate is deliberate: `CompanyProfile` is already a 20-field record covering identity, branding and draft copy. Targeting answers a different question — who to go find — changes for different reasons, and is read by different code. Bolting six more fields onto `CompanyProfile` would make a file that is already doing too much worse.

```
Targeting(
    IReadOnlyList<string> Industries,      // query dimension + fit criterion
    IReadOnlyList<string> Geographies,     // query dimension + cheap filter
    IReadOnlyList<string> BuyerRoles,      // job-ad matching + decision-maker hunting
    int? SizeMin, int? SizeMax,            // employee band; cheap filter
    IReadOnlyList<string> TriggerEvents,   // when to reach out — the "why now"
    IReadOnlyList<string> Exclusions)      // never a fit; becomes a cheap-gate veto in C
```

`Offer` stays on `CompanyProfile`: it describes what the customer sells and is consumed by the draft prompt, not by targeting.

Every field earns its place by changing engine behaviour downstream:

| Field | Query generation (B) | Cheap gate (C) | LLM qualification (C) | Draft prompt |
|---|---|---|---|---|
| Industries | dimension | — | fit criterion | context |
| Geographies | dimension | filter | fit criterion | — |
| BuyerRoles | jobs-feed query | — | decision-maker | — |
| Size band | — | filter | fit criterion | — |
| TriggerEvents | dimension | time-bound signal | fit criterion | honest "why now" |
| Exclusions | — | **veto** | hard NO | — |

`TriggerEvents` is the field that fixes "right company, wrong moment". Today's `BuyingSignals` are just more keywords with no notion that a trigger is time-bound; naming triggers explicitly lets C treat recency as a first-class condition and lets the draft say why it is writing today without inventing a reason.

`Exclusions` becoming a cheap-gate veto is the single biggest cost saving available: today `Disqualifiers` only reach the LLM, so enrichment is paid for on companies already known to be wrong.

**Watch item:** company size is the field most likely to go unfilled, because many customers do not think in employee bands. If it is empty for most customers after a few weeks, cut it rather than leave a dead field on a form.

### Storage

An EF owned type on `Customer`, lists persisted as newline-joined strings, matching the existing convention for `TargetKeywords` / `BuyingSignals` / `Disqualifiers`. One migration adds the columns, all nullable. No data is migrated or destroyed by the migration itself.

## Authoring

Prose in, structure out, human corrects — the flow the operator chose, because it keeps onboarding cheap while making the output structured.

1. A `ITargetingGenerator` port (Application) takes the customer's existing free text (niche, brief, offer) and returns a proposed `Targeting`.
2. The proposal is **never** saved silently. It populates an editable form; a human reviews field by field and saves.
3. Generation is re-runnable. Re-running proposes fresh values into the form without overwriting what is stored until saved.

The Infrastructure implementation sits beside `GroqSearchProfileGenerator` and follows its conventions: best-effort, returns null on failure, never throws into the caller.

### Surfaces

- **Customer**: a `Targeting` page in portal settings (new rail item under Settings), built from the `.pz` design system, with a "Suggest from my description" action producing the proposal and a single primary Save.
- **Admin**: the same editor on the customer detail page at `/admin/customers/{id}`, so the operator can tune any customer's targeting — the capability the operator explicitly asked for.

Both write through one Application use case so validation and behaviour cannot drift between them.

## Compatibility and rollout

Structured targeting is added **alongside** the legacy flat fields; nothing is deleted and no lossy SQL data migration is attempted on free text.

`HarvestTargeting` gains a structured path and picks per customer:

- Customer has structured targeting → derive the harvest inputs from it.
- Otherwise → fall back to today's flat fields, unchanged.

In sub-project A the structured path must derive **the same** keyword lists the legacy path produces today, so behaviour is identical whichever branch runs and A can ship without moving lead quality in either direction. B and C then exploit the structure properly.

This fallback is what allows B and C to be built while some customers are still on legacy fields, and it is the reason A carries no risk to existing customers.

## Error handling

- Generation failure (LLM down, unparseable response) → the form stays on its stored values and says the suggestion could not be produced. Never a partial or silently empty profile.
- Empty targeting is valid: the fallback covers it, so a customer who never fills the form keeps working exactly as today.
- Validation: size band must have `SizeMin <= SizeMax` when both are set; list entries are trimmed and de-duplicated; blank entries dropped. Invalid input returns a field error rather than throwing.
- Admin and customer edits are the same code path, so authorisation is the only difference between them.

## Testing

xUnit + NSubstitute + FluentAssertions, `MethodName_StateUnderTest_ExpectedBehavior`, strict AAA, only outbound ports mocked, never domain entities.

- Domain: `Targeting` construction trims/de-dupes/drops blanks; size-band validation including the inverted case; empty targeting is a valid state.
- Generation: proposal is returned but not persisted; LLM failure leaves stored values untouched and surfaces a message; unparseable output is treated as failure, not as an empty profile.
- Use case: save from customer settings and save from admin produce identical stored state.
- Compatibility: a customer with structured targeting and a customer with only legacy fields both produce harvest inputs; and — the load-bearing test — for the same underlying data the two paths agree, so A provably changes nothing about what gets harvested.
- Web: the settings page and the admin editor each render, save, and reject invalid input; the admin editor requires SuperAdmin.

## Consequences

**Positive.** Each pipeline stage can finally read the part of the profile it needs. Exclusions become cheap instead of expensive. Triggers become time-bound, which is what makes an honest "why now" possible. The operator can tune any customer's targeting without a database or CLI.

**Negative.** More fields to fill in, and a profile only helps if it is filled in honestly — hence generation plus review rather than a blank form. Two editors to keep consistent, mitigated by a single shared use case. And A ships no visible lead-quality gain on its own, which must be communicated rather than glossed over.

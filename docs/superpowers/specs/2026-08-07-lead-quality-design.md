# Lead quality — veto, prioritise, triage, enrich

**Date:** 2026-08-07
**Status:** Approved (sub-project C)

## The evidence

This design is grounded in production data from the Coldstart tenant, not in theory. As of 2026-08-07:

| Measure | Value | What it means |
|---|---|---|
| Leads ever harvested | 219 | |
| From the `funding` source | 219 (100%) | Four of five sources have never produced a single lead |
| Distinct companies | 98 | **55% of leads are duplicates** |
| LLM-rejected (`FIT: NO`) | 160 (73%) | Enrichment is paid for on three rejects per keeper |
| Leads with any matched signal | 0 | **The keyword matcher has never once fired** |
| Distinct scores observed | 1 (`0.50`) | Every lead sits on the funding source's base score |

Sampled companies include `tech.eu`, `news.crunchbase.com`, `Lightspeed Venture Partners`, `Menlo Ventures` and `White Star Capital` — publishers and VC firms, not prospects. The leads that *do* pass (Ore Energy, SeeTrue, FuVeX, Vangrid) are deep-tech and hardware companies raising rounds, which do not resemble the stated ICP: early-stage B2B in the Benelux where the founder still does sales.

All source API keys (Adzuna, LangSearch, Scrapingdog) are configured and valid. Their output is discarded every night.

## The diagnosis

The cheap gate uses a **quality** heuristic as a **cost** control, and the heuristic never fires — so it degenerates into "only sources whose base score already clears the bar survive".

`SignalScorer.Score` returns `max(keywordScore, baseScore)`, and `HarvestLeads` drops anything below `MinScore = 0.4`. Base scores are:

| Source | Base score | Clears 0.4? |
|---|---|---|
| funding | 0.50 | yes — always, unconditionally |
| web search | 0.35 | no |
| Adzuna | 0.35 | no |
| LangSearch | 0.25 | no |
| Hacker News | none defined | no |

`keywordScore` could rescue the others, but it is computed by `haystack.Contains(keyword)` over multi-word intent phrases ("first sales hire", "struggling with outbound", "founder-led sales"). A funding blurb reading "Ore Energy raises €10M" contains no such phrase, and neither does a typical HN title or job ad. Exact substring matching of long phrases against short text essentially never matches — confirmed by the zero-matched-signals figure above.

Therefore the engine is not a five-source pipeline with a tuning problem. It is a single-source funding-news scraper in which the ICP filter does no work whatsoever.

**The fix is not to retune thresholds. It is to stop using quality as a gate.**

## Budget

The model is `llama-3.3-70b-versatile` on Groq. An enrichment call is roughly 2,000–2,500 input tokens and ~400 output, on the order of **€0.002 per lead**. The operator's ceiling is **€1 per night for a paying or free-trial customer, €0 otherwise**, which buys several hundred judgments against today's cap of 30.

Money was never the binding constraint. The real limits are the configured 6-second inter-call delay (`LLM_REQUEST_DELAY_SECONDS=6`, ten calls per minute — a free-tier accommodation) and the arbitrary `MaxEnrichPerRun = 30`.

The per-token figure above is an estimate and must be validated against a real Groq invoice before the budget ceiling is trusted. The design therefore treats the budget as a **configured number of calls**, not a computed currency amount, so a wrong price estimate cannot cause overspend.

## The design: four stages, each with one job

### 1. Veto — cheap, certain, free

Runs before any spend, drops only what is provably wrong:

- **Hard exclusions.** The customer's `Exclusions` matched against the item. This is the cheapest, highest-value filter available and today it does not exist at this stage — exclusions only reach the LLM, so enrichment is paid for on companies already known to be wrong.
- **Publisher and VC denylist.** A maintained list (tech.eu, crunchbase, sifted, and the common VC firm names) so a feed's publisher or the investor is never mistaken for the prospect. This directly fixes five of the sampled bad companies.
- **Company-level deduplication.** Today dedup is by content fingerprint only, which is why the same company appears up to three times from re-published items. Add a case-insensitive company-name check against leads already held for that customer within a rolling window.
- **Freshness window.** Items older than the window are dropped rather than congratulated on stale news.

No keyword scoring happens here.

### 2. Prioritise — rank, never drop

Every surviving candidate from every source competes. The score becomes a **ranking** signal that decides which candidates get spend first, not a pass/fail bar:

- source base score acts as a prior (a funding item is a better prior than a random web result),
- trigger-event and keyword matches boost it,
- recency boosts it.

`MinScore` as a drop-threshold is removed. Candidates are ordered and the top N proceed. Nothing is discarded for ranking low — it simply queues behind better candidates and may be picked up on a later run.

This single change resurrects Hacker News, Adzuna, LangSearch and web search, because a source no longer has to clear an absolute bar to be seen.

### 3. Triage — cheap verdicts at volume

A short LLM pass over many candidates that returns only:

- a verdict (fit / not a fit / unsure),
- the **real company name** (which also corrects entity extraction at the source),
- which ICP criteria matched and which failed.

Because the output is tiny, triage costs roughly a tenth of full enrichment, so the budget covers hundreds of candidates. This is where the structured targeting profile finally does real work: size band, geography, stage and exclusions become explicit criteria the model is asked to check, rather than substring luck.

The stored verdict and reasons make "why did I get this lead?" answerable — and equally, why one was rejected.

### 4. Enrich — drafts only for survivors

Full draft-writing runs only on candidates that pass triage. The 73% waste approaches zero, because emails stop being written for companies that were never a fit.

## Budget enforcement

Two configured caps per customer per night, both counted in calls rather than currency:

- `MaxTriagePerRun` — default 200
- `MaxEnrichPerRun` — the existing cap, meaningful again now that its slots go to qualified candidates

A customer on the free plan with no entitlement gets zero LLM spend, matching the operator's "no money per customer for now" position; free-trial and paying customers get the full budget. Both caps are configuration, so the ceiling can be tightened the moment a real invoice contradicts the estimate.

Pacing stays configurable. At the current 6-second delay, triaging 200 candidates takes about 20 minutes of wall clock — acceptable for an overnight job, and the harvest already runs as a background queue. If the Groq account is on a paid tier the delay can be reduced and the same work finishes in a fraction of the time; the design does not depend on which tier is in use.

## Error handling

- A failing source must never abort a run (already true; preserved).
- Triage failure for one candidate must not abort the batch: on failure the candidate is treated as *unsure* and simply does not proceed to enrichment this run.
- If triage is unavailable entirely (no API key, provider down), the run degrades to today's behaviour — prioritised candidates go straight to enrichment up to `MaxEnrichPerRun` — rather than producing no leads at all.
- Budget exhaustion is a normal outcome, not an error: the run stops spending and reports how many candidates it did not reach.
- Every dropped candidate records *which* stage dropped it, so the funnel is explainable.

## Measurement — how we prove it worked

The current numbers in "The evidence" are the baseline. After rollout, the same query set must show:

- leads originating from **more than one source**,
- reject rate **well below 73%**,
- duplicate rate **well below 55%**,
- zero publishers or VC firms recorded as companies,
- a spread of scores rather than a single value.

A harvest run must record its funnel — harvested, vetoed, prioritised, triaged, enriched, saved, with the drop reason per stage — so these can be read directly rather than inferred. Without this instrumentation the work cannot be evaluated, so it is part of the deliverable, not an afterthought.

## Sequencing

The stages are independently shippable and ordered by value per unit of risk:

1. **Veto + dedup + entity fixes.** No LLM involvement, immediate reduction in waste and in visibly wrong companies. Ships first and stands alone.
2. **Prioritise.** Removes the drop-threshold and revives the four dead sources. Highest impact on lead variety; also the highest risk of a volume spike, so it lands with the instrumentation from step 1 already in place.
3. **Triage + budget caps.** The quality gate proper, and the step that makes the extra volume affordable.
4. **ICP-aware enrichment.** Feeds the triage verdict's reasons into the draft so "why now" is grounded in a checked criterion rather than an inferred one.

## Consequences

**Positive.** Five live sources instead of one. An ICP filter that filters. Enrichment spend redirected from rejects to real prospects. Publishers and investors stop appearing as prospects. Duplicates collapse. Every lead — and every rejection — becomes explainable.

**Negative.** More moving parts in the harvest pipeline, and a longer-running nightly job. Removing the drop-threshold will initially increase candidate volume sharply, which is why instrumentation ships first and the caps are configuration. Triage adds a second LLM dependency in the critical path, mitigated by the documented degrade-to-today's-behaviour path.

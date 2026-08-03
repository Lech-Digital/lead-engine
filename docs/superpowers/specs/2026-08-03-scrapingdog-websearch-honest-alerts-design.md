# Web-search on Scrapingdog + LangSearch background source + honest harvest alerts

**Date:** 2026-08-03 · **Status:** Accepted

## Context

The nightly harvest enriched 0 leads on 2026-08-02 and 2026-08-03. Investigation showed
enrichment never received a candidate: every scored-in item deduped against fingerprints
already harvested during the 2026-07-30 → 08-01 launch runs. The curated RSS/HN/Adzuna
sources return largely the same items day after day; the web-search source — added
precisely to supply fresh per-run content — never ran in production because
`BRAVE_SEARCH_KEY` was never set (Brave requires a credit card, which the operator
cannot provide). The operator alert misdiagnosed the incident as "LLM quota?" because it
compares `Enriched` against `ScoredIn` without subtracting `Deduped`.

Scrapingdog's Google Search API was verified live (2026-08-03):

- Endpoint `https://api.scrapingdog.com/search` (the documented `/google/` path returns a
  different, organic-less shape — do not use it).
- Auth via `api_key` query parameter; response is `{"organic_results": [{title,
  displayed_link, link, snippet, rank}]}`.
- `tbs=qdr:w` (Google time-filter syntax) restricts to the past week; `country=nl` works.
- Account: free plan, **200 credits/month**, one search = 5 credits → ~40 searches/month.
  Current config (6 queries/night) would exhaust the quota by day 7.

LangSearch (free, `POST https://api.langsearch.com/v1/web-search`, Bearer key) was also
verified live (2026-08-03): relevant results under `data.webPages.value[]`
(`name`/`url`/`snippet`), but its `freshness` parameter is **decorative** — `oneDay` and
`noLimit` return near-identical result sets topped by a November-2025 article, and
`datePublished` is null on every hit, so recency cannot be filtered client-side either.
Snippets arrive with mangled tokenization ("part ni fy secures € 1 . 8 m"). It therefore
cannot power the week-fresh signal source, but it is acceptable as a free, wider
*background-evidence* source when framed honestly.

## Decision

1. **`WebSearchSource` targets Scrapingdog** instead of Brave: GET
   `https://api.scrapingdog.com/search?api_key=…&query=…&results=…&country=…&tbs=…`,
   parse `organic_results`. `RawItem` mapping: title→title, link→url/fingerprint,
   snippet→text (behind the existing WEB SIGNAL sentinel), `displayed_link`→site/author.
   Brave's `age` field has no Google equivalent; the honesty of the sentinel ("published
   or updated this week") is preserved server-side by `tbs=qdr:w`. Soft-degrade without a
   key and per-query try/catch stay as they are.
2. **Budget: one search per nightly run, rotating keywords.** `MaxQueries` default drops
   6 → 1 (~155 credits/month for one Full customer, inside the 200 cap). The keyword
   window rotates by UTC day-of-year (`Skip(day % count)` then wrap) so every profile
   keyword is covered across a week instead of only the first. Clock injected via an
   internal test-seam constructor, mirroring `GroqLeadEnricher`'s delay seam.
3. **`Freshness` default `"pw"` → `"qdr:w"`** (tbs syntax replaces Brave syntax).
4. **New `LangSearchSource`** (`Name = "langsearch"`), a second independent `ILeadSource`:
   POST with Bearer key, per-run keywords rotated the same day-of-year way, `count` 1-10.
   Its sentinel must NOT claim freshness — it states the publication date is unknown, the
   page may be months old, and the LLM must not congratulate or treat any event as
   current; background evidence of fit only. Lower score defaults than the week-fresh
   source (`BaseScore 0.25`, `ScoreWeight 0.7` vs. websearch's 0.35/0.8). `freshness:
   "oneWeek"` is still sent — harmless now, free upside if LangSearch ever fixes it.
   Soft-degrades without a key. New `LangSearchOptions` on `NicheOptions` (`Enabled`,
   `ApiKey`, `MaxQueries` default 6 — it is free, `Count` default 10, scores).
5. **Env plumbing:** `docker-compose.prod.yml` and `deploy/.env.prod.example` rename
   `BRAVE_SEARCH_KEY` → `SCRAPINGDOG_KEY` and add `LANGSEARCH_KEY`
   (`Niche__LangSearch__ApiKey`); the real keys go into the prod `.env`.
6. **Honest alerts in `HarvestQueueService`:** the "LLM quota?" alert fires only when
   post-dedup candidates existed (`ScoredIn − Deduped > 0`, `Enriched == 0`). A new
   distinct alert covers the incident that actually happened: `ScoredIn > 0` and all of
   it deduped — "sources produced nothing new", pointing the operator at source
   freshness, not LLM keys.

## Consequences

**Positive:** web-search runs in production at last (fresh weekly Google net, no credit
card); LangSearch adds a free, unmetered wide net on top without polluting the
"this week" framing; quota cannot silently exhaust mid-month; the two zero-enrichment
failure modes (LLM down vs. content starvation) are now distinguishable from the alert
inbox.

**Negative:** one query/night is thin coverage — a Full customer with many keywords waits
up to a week for a given keyword's turn; more Full customers multiply the burn (revisit
the budget or plan at ~2 customers). Scrapingdog scrapes Google SERPs (ToS grey zone) —
acceptable for this volume; failed requests are not charged.

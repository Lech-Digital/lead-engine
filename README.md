# Automated AI Lead Engine

Harvests public buying signals → scores them cheaply → enriches the good ones with an LLM →
dedupes → stores → exports leads. Runs on **$0 and no API key** out of the box (heuristic
fallback), and is one env var away from real Groq/Claude/OpenAI drafts.

```
HN (official API)    ─┐
Greenhouse jobs API  ─┼─► signal score ─► dedup ─► enrich top-N ─► SQLite ─► CSV
Lever jobs API       ─┤      (free gate)          (capped LLM)              └─► Slack
(add more sources)   ─┘                                                         (idempotent)
```

Cost discipline is built in: cheap deterministic scoring + dedup run *before* any LLM
call, only the **highest-scoring N** leads per run get enriched (`max_enrich_per_run`),
and rate-limited/failed enrichments are retried next run rather than saved empty.

## Quick start

```bash
cd lead-engine
python3 -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt

# Runs immediately with NO key (templated drafts), proves the pipeline:
python -m leadengine.cli run

# For real AI drafts: cp .env.example .env, set LLM_PROVIDER + LLM_API_KEY (Groq is free)
python -m leadengine.cli run
python -m leadengine.cli export --min-score 0.5
```

Output lands in `leads.db` (SQLite) and `leads.csv`. Each client = one `config.*.yaml`:
`python -m leadengine.cli --config config.acme.yaml run`.

## Run tests

```bash
pip install pytest && pytest -q
```

## Where the money logic lives

- **`config.yaml`** — the entire business: client offer, niche, keywords, buying signals, threshold.
- **`signals.py`** — the free filter that decides what's worth paying AI for. Tune this first.
- **`enrich.py`** — the prompt that turns a raw post into a personalised draft. Your real moat.
- **`sources/`** — add harvesters here. Use official APIs/RSS, never login-walled scraping.

## Delivery to Slack

Create an Incoming Webhook (https://api.slack.com/messaging/webhooks), put the URL in
`SLACK_WEBHOOK_URL`. Then `run` auto-delivers, or push on demand:

```bash
python -m leadengine.cli deliver   # posts undelivered leads >= min_score, marks them sent
```

One webhook = one channel = one client. Delivery is idempotent — cron never double-posts.

## Adding job-board sources

In `config.yaml`, list Greenhouse board tokens (`boards.greenhouse.io/<token>`) or Lever
slugs (`jobs.lever.co/<slug>`). The engine matches your `target_keywords` against the
**role title** — an open "Head of People" req is the buying signal.

## What this v1 does NOT do yet (deliberate next steps)

1. **Scheduling** — wrap `cli run` in cron / GitHub Actions / n8n for 24/7 silent runs.
2. **More sources** — funding RSS, Reddit, product changelogs, BuiltWith tech-stack changes.
3. **Postgres/Supabase** — swap `storage.py` backend when you outgrow SQLite.
4. **Reply handling** — this drafts outreach; sending + inbox management is still on you.

## Honest expectations

The engine is the easy 20%. Revenue depends on the 80% no script does for you: picking a
niche where companies already pay for outreach, validating lead quality by hand for the first
weeks, and selling. Cold email converts low and a new sending domain burns easily — warm it,
keep volume sane, and expect 1–2 clients in month one to be a *good* result, not 12.
```

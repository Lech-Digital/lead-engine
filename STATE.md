# Lead Engine — Session State (resume here)

_Last updated: 2026-06-29_

## What this is
Automated AI Lead Engine: harvests public buying signals → scores cheaply → dedups →
AI-qualifies & drafts outreach → stores → (optionally) pushes to Slack.
Niche configured in `config.yaml`: B2B companies hiring HR/People/Talent leaders.

## Status: WORKING & VERIFIED LIVE
- Tests: 44/44 passing (`pytest -q`)
- Multi-client productization (2026-06-29): per-client Slack channel via config,
  `leadengine new "<name>"` scaffolds an isolated client config, `leadengine run-all`
  runs every client in `clients/` with separate DB/CSV/channel. Verified run-all live
  across 2 clients with fully isolated data.
- Approve-queue + email send (2026-06-29): nothing auto-sends. YES leads enter a
  review queue; operator approves with a REAL recipient address; `send` delivers via
  a transactional provider (Resend now; Postmark/SES drop in behind EmailSender).
  Verified queue->approve->send --dry-run end-to-end (idempotent).
- Live-tested end-to-end with Groq (`llama-3.3-70b-versatile`)
- Funding RSS source (2026-06-29): live TechCrunch /venture/ parses; funding-term
  filter cut 18 raw items → 6 real rounds before any LLM call.
- Precision pass (2026-06-29): added config disqualifiers, HN down-weighting, and
  contact-research links. Re-qualifying the SAME 77 harvested items: YES 9 → 3.
  HackerNews 4 YES → 0 (opinion/AI-skeptic noise gone); Orbio (a hiring-software
  competitor) correctly flipped YES → NO; all YES leads now carry contact links.

## To resume after restart
```bash
cd ~/Code/private/lead-engine
source .venv/bin/activate          # venv already built
python -m leadengine.cli run       # uses .env (Groq key already in place, gitignored)
python -m leadengine.cli export --min-score 0.5
python -m leadengine.cli deliver   # no-op until SLACK_WEBHOOK_URL is set
```

## Built so far
- Sources: HackerNews (official API), Greenhouse + Lever public job APIs,
  Funding RSS/Atom feeds (`sources/funding.py`, stdlib XML — no feedparser dep)
- Funding source specifics: honest framing sentinel (fed verbatim to the LLM),
  `lookback_days` window, funding-term filter (`require_funding_terms`, default on)
  to drop VC editorial, and a per-source `base_score` floor (`signals.score_item`)
  so a round clears the niche/2-axis gate even with no niche keywords in its text
- Cheap deterministic scoring gate before any LLM call (`signals.py`); per-source
  `base_score` floor + `score_weight` multiplier (HN demoted to 0.7 by default)
- AI enrichment: qualify-first prompt → FIT (YES/MAYBE/NO) + SUBJECT + DRAFT + COMPANY,
  with config-driven `niche.disqualifiers` (hard-NO list: competitors, forum venters,
  job seekers, AI-skeptics) (`enrich.py`)
- Contact discovery (`contact.py`): deterministic, $0, HONEST research links
  (LinkedIn people-search + web search for `niche.buyer_titles` + signal source).
  Never fabricates email addresses. Surfaced in CSV + Slack.
- Multi-client (`clients.py` + CLI `new` / `run-all`): one config per client in
  `clients/`, isolated `data/<slug>.db|csv`, per-client Slack webhook via
  `delivery.slack.webhook_env`. Config resolver: `config.slack_webhook`.
- Outreach approval queue: lead lifecycle status pending_review -> approved -> sent
  (or rejected) in `storage.py`. Email transport in `email/` (NullSender default,
  DryRunSender for $0 testing, ResendSender for live). `pipeline.send_approved` is
  idempotent. CLI: `queue`, `approve <id> --email`, `reject <id>`, `send [--dry-run]`.
  Recipient address is operator-supplied, NEVER AI-guessed.

## Deployment (2026-06-30)
- **Brand: Coldstart** (renamed from "Lead Engine"). Site name → getcoldstart.netlify.app.
- Git: private repo **github.com/adziusmaster/lead-engine** (HTTPS remote via gh token).
  Secrets (.env, *.db, *.csv, logs/) git-ignored, never pushed.
- Site hosted on Netlify: **https://getcoldstart.netlify.app**
  (publish dir = `site`). Can rename site / add custom domain in Netlify later.
- Contact wired in: CTA email **adzius.lech@gmail.com**, sender name **Andrzej Lech**.
- See `DEPLOY.md`. Engine still runs locally (launchd), not hosted.

## Sales assets (2026-06-29)
- `site/index.html` — standalone static pitch website (full HTML doc, self-contained,
  no deps), built from REAL leads.db (GitLab/Stripe/Patronus). CTA = "Send me an email"
  (mailto), NOT a booked call. Host on GitHub Pages / Netlify / any static host.
- `site/cold-email.md` — 3 cold-email variants + follow-up; ask is a reply-by-email.
- `site/README.md` — local preview + hosting instructions.
- TODO before sending: replace `hello@yourdomain.com` in index.html and
  `{{your site url}}` / `{{your name}}` in cold-email.md.

## Scheduling (2026-06-29) — runs daily 11:00 until Fri 2026-07-03
- NO ADMIN RIGHTS on this Mac, so cron is impossible (cron needs Full Disk Access
  granted to the system daemon). Using a USER LaunchAgent instead (no admin needed).
- `scripts/run_daily.sh` — activates venv, runs `leadengine run`, logs to logs/daily.log.
  Self-bounds: after 2026-07-03 it no-ops AND unloads/removes its own LaunchAgent.
- `scripts/install_schedule.sh` — writes+loads ~/Library/LaunchAgents/com.leadengine.daily.plist
- `scripts/uninstall_schedule.sh` — remove early.
- Status: LOADED (launchctl list shows com.leadengine.daily). Monday 6/29 run already
  done manually (11:00 had passed). Fires Tue–Fri at 11:00.
- CAVEAT: launchd only fires if the Mac is awake/logged-in at 11:00; if asleep it runs
  once on next wake. Logs: logs/daily.log (+ logs/launchd.{out,err}.log).

## CLI reference
- `leadengine run` / `run-all` — harvest+score+enrich+store (single / all clients)
- `leadengine queue` — list leads awaiting approve/reject (shows contact-research links)
- `leadengine approve <id> --email <addr>` — approve + set real recipient
- `leadengine reject <id>` — drop a lead from the queue
- `leadengine send [--dry-run]` — send approved leads via `delivery.email` provider
- `leadengine new "<Client>"` — scaffold clients/<slug>.yaml
- `leadengine export [--min-score]` / `deliver` — CSV / Slack push
- LLM providers: Groq / OpenAI / Anthropic + $0 heuristic fallback, with retry/backoff (`llm/`)
- SQLite storage w/ dedup + `delivered` flag + auto-migration (`storage.py`)
- Slack delivery, idempotent (`delivery/slack.py`)
- Spend controls: `max_enrich_per_run` cap (default 40), top-scoring leads first
- Failed/empty enrichments NOT saved → retried next run

## Next steps (pick one — awaiting your choice)
- (a) Scheduling: cron / GitHub Actions / n8n for silent 24/7 runs
- (b) DONE — funding-announcement RSS source shipped (2026-06-29)
- (c) Pause building — eyeball a few days of leads by hand, confirm quality for the niche
- (d) Add more funding feeds beyond TechCrunch (e.g. other VC-news RSS) for breadth

## Honest reminder
The engine is the easy 20%. It DRAFTS outreach but does not SEND it and does not find
clients. Revenue still depends on: picking a niche that pays for outreach, validating lead
quality by hand, warming a sending domain, and selling. 1–2 clients in month one = good.

## Housekeeping
- Groq key lives in `.env` (gitignored). It was pasted in chat in plaintext — rotate if concerned.
- Not a git repo yet. Consider `git init` before committing anything.

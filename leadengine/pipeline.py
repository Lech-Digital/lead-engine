from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone

from .config import Config, LLMSettings
from .delivery import SlackDelivery
from .email import EmailSender, build_email_sender
from .enrich import enrich
from .llm import build_llm
from .signals import score_item
from .sources import build_sources
from .storage import Storage


@dataclass
class RunStats:
    harvested: int = 0
    scored_in: int = 0       # passed the score threshold
    enriched: int = 0        # new (non-duplicate) leads enriched + saved
    duplicates: int = 0
    skipped_over_cap: int = 0  # relevant leads left un-enriched due to per-run cap
    enrich_failed: int = 0     # LLM returned nothing; will retry next run
    delivered: int = 0       # pushed to Slack this run

    def __str__(self) -> str:
        cap_note = f" over_cap={self.skipped_over_cap}" if self.skipped_over_cap else ""
        fail_note = f" enrich_failed={self.enrich_failed}" if self.enrich_failed else ""
        return (
            f"harvested={self.harvested} relevant={self.scored_in} "
            f"new_leads={self.enriched} duplicates_skipped={self.duplicates}"
            f"{cap_note}{fail_note} delivered={self.delivered}"
        )


def run(config: Config, llm_settings: LLMSettings | None = None) -> RunStats:
    """One full pass: harvest -> score -> dedup -> enrich -> store.

    Order matters for cost: cheap deterministic scoring and dedup happen BEFORE any
    paid LLM call, so you only spend inference on genuinely new, relevant items.
    """
    llm = build_llm(llm_settings or LLMSettings.from_env())
    storage = Storage(config.sqlite_path)
    stats = RunStats()

    # Phase 1: harvest + cheap scoring + dedup. No paid calls yet.
    candidates = []  # (score, item, signal) for new, relevant items
    for source in build_sources(config):
        for item in source.fetch():
            stats.harvested += 1
            signal = score_item(
                item, config.target_keywords, config.buying_signals,
                base_score=source.base_score, score_weight=source.score_weight,
            )
            if signal.score < config.min_score:
                continue
            stats.scored_in += 1
            if storage.already_seen(item.fingerprint):
                stats.duplicates += 1
                continue
            candidates.append((signal.score, item, signal))

    # Phase 2: enrich only the best N, so LLM spend is capped and goes to top leads.
    candidates.sort(key=lambda c: c[0], reverse=True)
    cap = config.max_enrich_per_run
    if len(candidates) > cap:
        stats.skipped_over_cap = len(candidates) - cap
    for _, item, signal in candidates[:cap]:
        lead = enrich(item, signal, config, llm)
        if not lead.draft.strip():
            # LLM returned nothing (rate limit / transient error). Don't persist —
            # leaving it unseen means the next run retries it instead of burying it.
            stats.enrich_failed += 1
            continue
        if storage.save(lead):
            stats.enriched += 1

    stats.delivered = deliver(config, storage)
    return stats


def deliver(config: Config, storage: Storage | None = None) -> int:
    """Push any undelivered leads above threshold to Slack. Idempotent: a lead is
    marked delivered on success, so re-running (or cron) never double-posts.

    The webhook is resolved per-client from config (with legacy-env fallback), so each
    client's leads land in that client's own channel."""
    slack = SlackDelivery(config.slack_webhook)
    if not slack.enabled:
        return 0
    storage = storage or Storage(config.sqlite_path)
    count = 0
    for lead in storage.undelivered(config.min_score):
        if slack.send(lead):
            storage.mark_delivered(lead.fingerprint)
            count += 1
    return count


def send_approved(
    config: Config,
    sender: EmailSender | None = None,
    storage: Storage | None = None,
    now: str | None = None,
) -> tuple[int, int]:
    """Send every operator-approved lead via the configured transactional email
    provider. Returns (sent, failed). Idempotent: a lead is stamped `sent` on success
    so re-running never double-sends; a failure leaves it queued for the next attempt.

    Nothing here is automatic — only leads a human explicitly approved (with a real
    recipient address) are ever sent."""
    sender = sender or build_email_sender(config)
    if not sender.enabled:
        return (0, 0)
    storage = storage or Storage(config.sqlite_path)
    stamp = now or datetime.now(timezone.utc).isoformat()
    sent = failed = 0
    for lead in storage.approved_unsent():
        if not lead.recipient_email:
            continue  # approval requires a real address; never send without one
        if sender.send(lead.recipient_email, lead.subject, lead.draft):
            storage.mark_sent(lead.fingerprint, stamp)
            sent += 1
        else:
            failed += 1
    return (sent, failed)

from __future__ import annotations

import hashlib
from dataclasses import dataclass, field, asdict
from datetime import datetime, timezone


@dataclass
class RawItem:
    """A single piece of harvested content from any source, before scoring/enrichment."""
    source: str                 # e.g. "hackernews"
    external_id: str            # stable id within the source (for dedup)
    text: str                   # the content we analyse
    title: str = ""
    author: str = ""
    url: str = ""
    created_at: str = ""        # ISO8601 if known

    @property
    def fingerprint(self) -> str:
        """Stable hash used to avoid re-processing the same item twice."""
        return hashlib.sha256(f"{self.source}:{self.external_id}".encode()).hexdigest()


@dataclass
class Lead:
    """An enriched, scored lead ready to deliver to a client."""
    fingerprint: str
    source: str
    author: str
    title: str
    url: str
    created_at: str
    score: float
    matched_signals: list[str] = field(default_factory=list)
    fit: str = ""               # AI: YES / MAYBE / NO qualification verdict
    summary: str = ""           # AI: what this company is / why they're a fit
    subject: str = ""           # AI: outreach subject line
    draft: str = ""             # AI: the personalised outreach message
    company: str = ""           # AI: the company/org name (for contact research)
    contact: str = ""           # honest research links to find the buyer (no fake emails)
    status: str = ""            # approval lifecycle: pending_review/approved/rejected/sent
    recipient_email: str = ""   # operator-supplied real address (never AI-guessed)
    sent_at: str = ""           # ISO8601 timestamp when outreach was sent
    raw_text: str = ""
    harvested_at: str = field(
        default_factory=lambda: datetime.now(timezone.utc).isoformat()
    )

    def to_row(self) -> dict:
        d = asdict(self)
        d["matched_signals"] = ", ".join(self.matched_signals)
        return d

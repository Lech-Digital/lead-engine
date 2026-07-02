from __future__ import annotations

import csv
import re
import sqlite3
from contextlib import contextmanager
from pathlib import Path

from .models import Lead

# Company-name suffixes stripped before comparing, so "Lucida AI" and "Lucida" (which
# the engine harvests from different feeds) count as the same place already contacted.
_CO_SUFFIX = re.compile(r"\b(ai|inc|ltd|llc|labs|gmbh|technologies|technology|corp|group)\b")


def normalize_company(name: str) -> str:
    """A loose comparison key for a company name: lowercased, common suffixes dropped,
    non-alphanumerics removed. Used only for dedup, never shown to anyone."""
    stripped = _CO_SUFFIX.sub(" ", (name or "").lower())
    return re.sub(r"[^a-z0-9]", "", stripped)


def email_domain(addr: str) -> str:
    """The domain of an email address, lowercased ('' if not an address)."""
    return addr.rsplit("@", 1)[-1].strip().lower() if addr and "@" in addr else ""

SCHEMA = """
CREATE TABLE IF NOT EXISTS leads (
    fingerprint     TEXT PRIMARY KEY,
    source          TEXT,
    author          TEXT,
    title           TEXT,
    url             TEXT,
    created_at      TEXT,
    score           REAL,
    matched_signals TEXT,
    fit             TEXT,
    summary         TEXT,
    subject         TEXT,
    draft           TEXT,
    company         TEXT,
    contact         TEXT,
    status          TEXT,
    recipient_email TEXT,
    sent_at         TEXT,
    raw_text        TEXT,
    harvested_at    TEXT,
    delivered       INTEGER DEFAULT 0
);
"""

# Columns added after v0.1 — applied to pre-existing databases on startup.
_MIGRATIONS = {
    "fit": "ALTER TABLE leads ADD COLUMN fit TEXT",
    "subject": "ALTER TABLE leads ADD COLUMN subject TEXT",
    "delivered": "ALTER TABLE leads ADD COLUMN delivered INTEGER DEFAULT 0",
    "company": "ALTER TABLE leads ADD COLUMN company TEXT",
    "contact": "ALTER TABLE leads ADD COLUMN contact TEXT",
    "status": "ALTER TABLE leads ADD COLUMN status TEXT",
    "recipient_email": "ALTER TABLE leads ADD COLUMN recipient_email TEXT",
    "sent_at": "ALTER TABLE leads ADD COLUMN sent_at TEXT",
}


class Storage:
    def __init__(self, path: str):
        self.path = path
        # Per-client configs nest data under data/<slug>.db; make the folder if needed.
        parent = Path(path).parent
        if str(parent) not in ("", "."):
            parent.mkdir(parents=True, exist_ok=True)
        with self._conn() as c:
            c.executescript(SCHEMA)
            existing = {r["name"] for r in c.execute("PRAGMA table_info(leads)").fetchall()}
            for col, ddl in _MIGRATIONS.items():
                if col not in existing:
                    c.execute(ddl)

    @contextmanager
    def _conn(self):
        conn = sqlite3.connect(self.path)
        conn.row_factory = sqlite3.Row
        try:
            yield conn
            conn.commit()
        finally:
            conn.close()

    def already_seen(self, fingerprint: str) -> bool:
        with self._conn() as c:
            row = c.execute(
                "SELECT 1 FROM leads WHERE fingerprint = ?", (fingerprint,)
            ).fetchone()
            return row is not None

    def save(self, lead: Lead) -> bool:
        """Insert a lead. Returns False if it was a duplicate (dedup is the whole point)."""
        row = lead.to_row()
        with self._conn() as c:
            cur = c.execute(
                """INSERT OR IGNORE INTO leads
                   (fingerprint, source, author, title, url, created_at, score,
                    matched_signals, fit, summary, subject, draft, company, contact,
                    status, recipient_email, sent_at, raw_text, harvested_at)
                   VALUES (:fingerprint, :source, :author, :title, :url, :created_at,
                           :score, :matched_signals, :fit, :summary, :subject, :draft,
                           :company, :contact, :status, :recipient_email, :sent_at,
                           :raw_text, :harvested_at)""",
                row,
            )
            return cur.rowcount > 0

    def undelivered(self, min_score: float = 0.0) -> list[Lead]:
        """Leads worth pushing to a client that haven't been delivered yet."""
        with self._conn() as c:
            rows = c.execute(
                """SELECT * FROM leads
                   WHERE COALESCE(delivered, 0) = 0 AND score >= ?
                   ORDER BY score DESC, harvested_at DESC""",
                (min_score,),
            ).fetchall()
        return [_row_to_lead(r) for r in rows]

    def mark_delivered(self, fingerprint: str) -> None:
        with self._conn() as c:
            c.execute(
                "UPDATE leads SET delivered = 1 WHERE fingerprint = ?", (fingerprint,)
            )

    # --- outreach approval lifecycle: pending_review -> approved -> sent / rejected ---

    def pending_review(self, min_score: float = 0.0) -> list[Lead]:
        """Leads awaiting the operator's approve/reject decision."""
        with self._conn() as c:
            rows = c.execute(
                """SELECT * FROM leads
                   WHERE status = 'pending_review' AND score >= ?
                   ORDER BY score DESC, harvested_at DESC""",
                (min_score,),
            ).fetchall()
        return [_row_to_lead(r) for r in rows]

    def approved_unsent(self) -> list[Lead]:
        """Approved leads (recipient set) that have not been sent yet."""
        with self._conn() as c:
            rows = c.execute(
                "SELECT * FROM leads WHERE status = 'approved' AND COALESCE(sent_at, '') = ''"
                " ORDER BY score DESC"
            ).fetchall()
        return [_row_to_lead(r) for r in rows]

    def sent(self) -> list[Lead]:
        """Every lead already emailed, oldest first — the outreach history."""
        with self._conn() as c:
            rows = c.execute(
                "SELECT * FROM leads WHERE status = 'sent' ORDER BY COALESCE(sent_at, '')"
            ).fetchall()
        return [_row_to_lead(r) for r in rows]

    def set_status(self, fingerprint: str, status: str, recipient_email: str | None = None) -> None:
        with self._conn() as c:
            if recipient_email is None:
                c.execute("UPDATE leads SET status = ? WHERE fingerprint = ?", (status, fingerprint))
            else:
                c.execute(
                    "UPDATE leads SET status = ?, recipient_email = ? WHERE fingerprint = ?",
                    (status, recipient_email, fingerprint),
                )

    def mark_sent(self, fingerprint: str, when: str) -> None:
        with self._conn() as c:
            c.execute(
                "UPDATE leads SET status = 'sent', sent_at = ? WHERE fingerprint = ?",
                (when, fingerprint),
            )

    def resolve_fingerprint(self, prefix: str) -> str | None:
        """Map a short fingerprint prefix (git-style) to the full id, or None if it
        is missing or ambiguous — we never act on a guess."""
        with self._conn() as c:
            rows = c.execute(
                "SELECT fingerprint FROM leads WHERE fingerprint LIKE ? LIMIT 2",
                (prefix + "%",),
            ).fetchall()
        return rows[0]["fingerprint"] if len(rows) == 1 else None

    def export_csv(self, csv_path: str, min_score: float = 0.0) -> int:
        with self._conn() as c:
            rows = c.execute(
                "SELECT * FROM leads WHERE score >= ? ORDER BY score DESC, harvested_at DESC",
                (min_score,),
            ).fetchall()
        if not rows:
            return 0
        with open(csv_path, "w", newline="", encoding="utf-8") as f:
            writer = csv.DictWriter(f, fieldnames=rows[0].keys())
            writer.writeheader()
            for r in rows:
                writer.writerow(dict(r))
        return len(rows)


def _row_to_lead(r) -> Lead:
    signals = r["matched_signals"] or ""
    return Lead(
        fingerprint=r["fingerprint"],
        source=r["source"],
        author=r["author"] or "",
        title=r["title"] or "",
        url=r["url"] or "",
        created_at=r["created_at"] or "",
        score=r["score"] or 0.0,
        matched_signals=[s.strip() for s in signals.split(",") if s.strip()],
        fit=r["fit"] or "",
        summary=r["summary"] or "",
        subject=r["subject"] or "",
        draft=r["draft"] or "",
        company=_get(r, "company"),
        contact=_get(r, "contact"),
        status=_get(r, "status"),
        recipient_email=_get(r, "recipient_email"),
        sent_at=_get(r, "sent_at"),
        raw_text=r["raw_text"] or "",
        harvested_at=r["harvested_at"] or "",
    )


def _get(row, key: str) -> str:
    """Tolerate rows from a DB migrated mid-session that may lack a column."""
    try:
        return row[key] or ""
    except (IndexError, KeyError):
        return ""

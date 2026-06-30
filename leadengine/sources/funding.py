from __future__ import annotations

import html
import re
from datetime import datetime, timedelta, timezone
from email.utils import parsedate_to_datetime
from urllib.parse import urlparse
from xml.etree import ElementTree as ET

import requests

from ..models import RawItem
from .base import Source

# A fresh funding round is the single strongest buying signal this engine harvests:
# a company that just raised is about to hire and re-tool. This sentinel FRAMES that
# fact for the LLM (the harvested text is fed verbatim into the draft prompt), so it
# must stay strictly honest — it restates the announcement, it never fabricates
# first-person claims the company did not make.
_FUNDING_SENTINEL = (
    "FUNDING SIGNAL — this company recently announced new investment, a strong "
    "indicator they are about to scale headcount and invest in new tooling:"
)


class FundingRSSSource(Source):
    """Harvests funding-announcement RSS/Atom feeds (e.g. TechCrunch's venture feed).

    Feeds are public XML that WANTS to be machine-read, so this stays within the
    'no scraping' discipline of base.Source. Unlike job boards, a funding item carries
    no niche keywords, so it relies on `base_score` to clear the relevance gate.
    """

    name = "funding"

    def __init__(self, cfg: dict):
        self.feeds: list[str] = cfg.get("feeds", [])
        self.lookback_days: int = int(cfg.get("lookback_days", 30))
        self.max_per_feed: int = int(cfg.get("max_per_feed", 50))
        # General-interest feeds mix funding rounds with VC editorial; keep only items
        # whose text reads like an actual round so editorial never costs an LLM call.
        # Disable for an already-curated, funding-only feed.
        self.require_funding_terms: bool = bool(cfg.get("require_funding_terms", True))
        # Intrinsic confidence floor for the deterministic scorer (see signals.score_item).
        self.base_score: float = float(cfg.get("base_score", 0.5))

    def fetch(self) -> list[RawItem]:
        items: list[RawItem] = []
        for url in self.feeds:
            try:
                resp = requests.get(url, timeout=20, headers={"User-Agent": "lead-engine/1.0"})
                resp.raise_for_status()
                xml_text = resp.text
            except requests.RequestException:
                continue  # one dead feed must not kill the run
            items.extend(self._items_from_feed(xml_text, url))
        return items

    def _items_from_feed(
        self, xml_text: str, feed_url: str, now: datetime | None = None
    ) -> list[RawItem]:
        now = now or datetime.now(timezone.utc)
        cutoff = now - timedelta(days=self.lookback_days)
        label = urlparse(feed_url).netloc or "feed"
        items: list[RawItem] = []

        for entry in _parse_feed(xml_text)[: self.max_per_feed]:
            published = entry["published"]
            # Drop stale items; keep anything with an unparseable/missing date so a
            # quirky feed format never silently swallows fresh leads.
            if published is not None and published < cutoff:
                continue
            summary = entry["summary"]
            if self.require_funding_terms and not _looks_like_funding(
                f"{entry['title']} {summary}"
            ):
                continue
            ident = entry["id"] or entry["url"] or entry["title"]
            text = f"{_FUNDING_SENTINEL} {entry['title']}. {summary}".strip()
            items.append(
                RawItem(
                    source=self.name,
                    external_id=f"{label}:{ident}",
                    text=text,
                    title=entry["title"],
                    author=label,
                    url=entry["url"],
                    created_at=published.isoformat() if published else "",
                )
            )
        return items


# Patterns that mark text as describing a real funding round, not VC commentary.
_FUNDING_PATTERNS = (
    r"\brais(?:e|es|ed|ing)\b",
    r"\b(?:pre-)?seed\b",
    r"\bseries\s+[a-h]\b",
    r"\bfunding\b",
    r"\bround\b",
    r"\bled\sby\b",
    r"\$\d+(?:\.\d+)?\s*[mMbB]\b",          # $50M, $2.5B
    r"\blands?\s+\$",                         # "lands $50M"
)
_FUNDING_RE = re.compile("|".join(_FUNDING_PATTERNS), re.IGNORECASE)


def _looks_like_funding(text: str) -> bool:
    return bool(_FUNDING_RE.search(text or ""))


def _parse_feed(xml_text: str) -> list[dict]:
    """Parse an RSS 2.0 or Atom feed into a list of normalised entry dicts.

    Stdlib-only (xml.etree) to avoid a feedparser dependency. Tolerant: malformed XML
    yields an empty list rather than raising, per the Source contract.
    """
    try:
        root = ET.fromstring(xml_text)
    except ET.ParseError:
        return []

    entries: list[dict] = []
    for node in root.iter():
        if _local(node.tag) in ("item", "entry"):
            entries.append(_parse_entry(node))
    return entries


def _parse_entry(node) -> dict:
    title = url = summary = ident = ""
    published: datetime | None = None

    for child in node:
        tag = _local(child.tag)
        if tag == "title":
            title = _clean(child.text)
        elif tag == "link":
            # RSS puts the URL in element text; Atom uses a href attribute.
            url = (child.text or "").strip() or child.attrib.get("href", "")
        elif tag in ("description", "summary", "content") and not summary:
            summary = _clean(child.text)
        elif tag in ("guid", "id") and not ident:
            ident = (child.text or "").strip()
        elif tag in ("pubdate", "published", "updated") and published is None:
            published = _parse_date(child.text)

    return {"title": title, "url": url, "summary": summary, "id": ident, "published": published}


def _local(tag: str) -> str:
    """Strip any XML namespace, lower-cased: '{ns}PubDate' -> 'pubdate'."""
    return tag.split("}")[-1].lower()


def _parse_date(value: str | None) -> datetime | None:
    if not value:
        return None
    value = value.strip()
    # RFC822 (RSS): "Wed, 04 Jun 2025 13:00:00 GMT"
    try:
        dt = parsedate_to_datetime(value)
        if dt is not None:
            return dt if dt.tzinfo else dt.replace(tzinfo=timezone.utc)
    except (TypeError, ValueError):
        pass
    # ISO8601 (Atom): "2025-06-04T09:30:00Z"
    try:
        dt = datetime.fromisoformat(value.replace("Z", "+00:00"))
        return dt if dt.tzinfo else dt.replace(tzinfo=timezone.utc)
    except ValueError:
        return None


def _clean(s: str | None) -> str:
    s = re.sub(r"<[^>]+>", " ", s or "")
    return re.sub(r"\s+", " ", html.unescape(s)).strip()

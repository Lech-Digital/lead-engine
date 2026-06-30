from __future__ import annotations

import time
from datetime import datetime, timedelta, timezone

import requests

from ..models import RawItem
from .base import Source

ALGOLIA = "https://hn.algolia.com/api/v1/search_by_date"


class HackerNewsSource(Source):
    """Harvests recent HN comments/stories via the official Algolia API (no key, allowed).

    HN is a strong buying-signal source: 'Who is hiring' threads, 'Show HN' launches,
    and people openly complaining about tools they want to replace.
    """

    name = "hackernews"

    def __init__(self, cfg: dict):
        self.queries: list[str] = cfg.get("queries", [])
        self.max_per_query: int = int(cfg.get("max_per_query", 30))
        self.lookback_hours: int = int(cfg.get("lookback_hours", 168))
        # HN comments are opinion-heavy and the lowest-intent source; demote by default
        # so genuine open roles / funding rounds are enriched and reviewed first.
        self.score_weight: float = float(cfg.get("score_weight", 0.7))

    def fetch(self) -> list[RawItem]:
        since = datetime.now(timezone.utc) - timedelta(hours=self.lookback_hours)
        cutoff_ts = int(since.timestamp())
        items: list[RawItem] = []

        for query in self.queries:
            try:
                resp = requests.get(
                    ALGOLIA,
                    params={
                        "query": query,
                        "tags": "comment",
                        "numericFilters": f"created_at_i>{cutoff_ts}",
                        "hitsPerPage": self.max_per_query,
                    },
                    timeout=20,
                )
                resp.raise_for_status()
                hits = resp.json().get("hits", [])
            except (requests.RequestException, ValueError):
                continue  # one bad query must not kill the run

            for h in hits:
                text = h.get("comment_text") or h.get("story_text") or ""
                if not text:
                    continue
                oid = str(h.get("objectID", ""))
                items.append(
                    RawItem(
                        source=self.name,
                        external_id=oid,
                        text=_strip_html(text),
                        title=h.get("story_title", "") or "",
                        author=h.get("author", "") or "",
                        url=f"https://news.ycombinator.com/item?id={oid}",
                        created_at=h.get("created_at", "") or "",
                    )
                )
            time.sleep(0.3)  # be polite to the public API

        return items


def _strip_html(s: str) -> str:
    import html
    import re

    s = re.sub(r"<[^>]+>", " ", s)
    s = html.unescape(s)
    return re.sub(r"\s+", " ", s).strip()

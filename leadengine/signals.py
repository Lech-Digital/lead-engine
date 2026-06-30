from __future__ import annotations

from dataclasses import dataclass

from .models import RawItem


@dataclass
class SignalResult:
    score: float
    matched: list[str]


def score_item(
    item: RawItem,
    target_keywords: list[str],
    buying_signals: list[str],
    base_score: float = 0.0,
    score_weight: float = 1.0,
) -> SignalResult:
    """Cheap, deterministic, $0 relevance scoring run BEFORE any LLM call.

    The point: only pay for AI enrichment on items that already look promising.
    Score = 0.5 * (does it match the niche?) + 0.5 * (does it show buying intent?),
    each weighted by how many distinct terms matched (with diminishing returns).

    `base_score` is a per-source confidence floor. Some sources (e.g. a funding
    announcement) are intrinsically strong buying signals even when their text
    carries none of the client's niche keywords; the floor stops the two-axis
    keyword score from discarding them. It only ever raises a score, never lowers it.

    `score_weight` (default 1.0) multiplies the keyword score to demote a noisy
    source (e.g. forum comments) so its items sort below higher-intent sources
    under the enrichment cap. The `base_score` floor still applies afterwards, so a
    high-confidence source is never dragged under its own floor by a weight.
    """
    haystack = f"{item.title}\n{item.text}".lower()

    target_hits = [k for k in target_keywords if k.lower() in haystack]
    signal_hits = [s for s in buying_signals if s.lower() in haystack]

    target_score = _saturating(len(target_hits))
    signal_score = _saturating(len(signal_hits))

    keyword_score = score_weight * (0.5 * target_score + 0.5 * signal_score)
    score = max(keyword_score, base_score)
    return SignalResult(score=round(score, 3), matched=target_hits + signal_hits)


def _saturating(n: int) -> float:
    """0 hits -> 0.0, 1 -> 0.6, 2 -> 0.8, 3 -> 0.9, ... capped at 1.0."""
    if n <= 0:
        return 0.0
    return min(1.0, 1.0 - 0.4 / n)

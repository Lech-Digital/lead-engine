from __future__ import annotations

from abc import ABC, abstractmethod

from ..models import RawItem


class Source(ABC):
    """A harvester. Add a new one by subclassing and registering in build_sources().

    Rule of thumb for picking sources: use endpoints that WANT to be machine-read
    (official APIs, RSS, public job-board JSON). Avoid login-walled / anti-bot pages —
    they break and create legal risk. That single discipline is why this engine keeps
    running while scraper-based ones die.
    """

    name: str = "base"

    # Intrinsic relevance floor (0.0-1.0) for this source's items, applied by the
    # deterministic scorer. 0.0 means "score purely on keywords"; a source whose mere
    # presence is itself a buying signal (e.g. funding announcements) raises this.
    base_score: float = 0.0

    # Multiplier (default 1.0) on this source's keyword score. < 1.0 demotes a noisy
    # source (e.g. forum comments) so it sorts below higher-intent sources.
    score_weight: float = 1.0

    @abstractmethod
    def fetch(self) -> list[RawItem]:
        """Return raw items. Must not raise on a single bad record — skip and continue."""
        raise NotImplementedError

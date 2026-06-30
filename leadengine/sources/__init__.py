from .base import Source
from .funding import FundingRSSSource
from .hackernews import HackerNewsSource
from .jobboards import GreenhouseSource, LeverSource

__all__ = [
    "Source",
    "HackerNewsSource",
    "GreenhouseSource",
    "LeverSource",
    "FundingRSSSource",
    "build_sources",
]


def build_sources(config) -> list[Source]:
    """Instantiate enabled sources from config."""
    sources: list[Source] = []
    cfg = config.sources or {}
    targets = config.target_keywords

    hn = cfg.get("hackernews", {})
    if hn.get("enabled"):
        sources.append(HackerNewsSource(hn))

    gh = cfg.get("greenhouse", {})
    if gh.get("enabled"):
        sources.append(GreenhouseSource(gh, targets))

    lv = cfg.get("lever", {})
    if lv.get("enabled"):
        sources.append(LeverSource(lv, targets))

    fund = cfg.get("funding", {})
    if fund.get("enabled"):
        sources.append(FundingRSSSource(fund))

    return sources

from __future__ import annotations

import re

import requests

from ..models import RawItem
from .base import Source

# An open, relevant job requisition is itself a buying signal, so we prepend this
# sentinel to the harvested text — it makes the deterministic scorer treat the role
# as active hiring intent without special-casing job-board items downstream.
_HIRING_SENTINEL = "This company is actively hiring for the open role below:"


def _matches(text: str, keywords: list[str]) -> bool:
    low = text.lower()
    return any(k.lower() in low for k in keywords) if keywords else True


def _strip_html(s: str) -> str:
    import html

    s = re.sub(r"<[^>]+>", " ", s)
    return re.sub(r"\s+", " ", html.unescape(s)).strip()


class GreenhouseSource(Source):
    """Greenhouse public job boards: https://boards-api.greenhouse.io/v1/boards/{token}/jobs"""

    name = "greenhouse"

    def __init__(self, cfg: dict, target_keywords: list[str]):
        self.boards: list[str] = cfg.get("boards", [])
        self.target_keywords = target_keywords

    def fetch(self) -> list[RawItem]:
        items: list[RawItem] = []
        for token in self.boards:
            try:
                resp = requests.get(
                    f"https://boards-api.greenhouse.io/v1/boards/{token}/jobs",
                    params={"content": "true"},
                    timeout=20,
                )
                resp.raise_for_status()
                jobs = resp.json().get("jobs", [])
            except (requests.RequestException, ValueError):
                continue

            for job in jobs:
                title = job.get("title", "") or ""
                content = _strip_html(job.get("content", "") or "")
                # Match the ROLE TITLE, not the description: a company hiring a
                # "Head of People" is the signal; an AE job that merely says
                # "talented team" is not.
                if not _matches(title, self.target_keywords):
                    continue
                location = (job.get("location") or {}).get("name", "")
                items.append(
                    RawItem(
                        source=self.name,
                        external_id=f"{token}:{job.get('id')}",
                        text=f"{_HIRING_SENTINEL} {title} ({location}). {content}",
                        title=f"{token}: {title}",
                        author=token,
                        url=job.get("absolute_url", "") or "",
                        created_at=job.get("updated_at", "") or "",
                    )
                )
        return items


class LeverSource(Source):
    """Lever public postings: https://api.lever.co/v0/postings/{company}?mode=json"""

    name = "lever"

    def __init__(self, cfg: dict, target_keywords: list[str]):
        self.companies: list[str] = cfg.get("companies", [])
        self.target_keywords = target_keywords

    def fetch(self) -> list[RawItem]:
        items: list[RawItem] = []
        for company in self.companies:
            try:
                resp = requests.get(
                    f"https://api.lever.co/v0/postings/{company}",
                    params={"mode": "json"},
                    timeout=20,
                )
                resp.raise_for_status()
                postings = resp.json()
            except (requests.RequestException, ValueError):
                continue

            for post in postings:
                title = post.get("text", "") or ""
                content = post.get("descriptionPlain", "") or ""
                if not _matches(title, self.target_keywords):
                    continue
                cats = post.get("categories") or {}
                location = cats.get("location", "")
                items.append(
                    RawItem(
                        source=self.name,
                        external_id=f"{company}:{post.get('id')}",
                        text=f"{_HIRING_SENTINEL} {title} ({location}). {content}",
                        title=f"{company}: {title}",
                        author=company,
                        url=post.get("hostedUrl", "") or "",
                        created_at=str(post.get("createdAt", "") or ""),
                    )
                )
        return items

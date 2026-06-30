from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path

import yaml


@dataclass
class Config:
    raw: dict

    @classmethod
    def load(cls, path: str | Path) -> "Config":
        with open(path, "r", encoding="utf-8") as f:
            return cls(raw=yaml.safe_load(f) or {})

    # convenience accessors -------------------------------------------------
    @property
    def client(self) -> dict:
        return self.raw.get("client", {})

    @property
    def niche(self) -> dict:
        return self.raw.get("niche", {})

    @property
    def sources(self) -> dict:
        return self.raw.get("sources", {})

    @property
    def target_keywords(self) -> list[str]:
        return self.niche.get("target_keywords", [])

    @property
    def buying_signals(self) -> list[str]:
        return self.niche.get("buying_signals", [])

    @property
    def disqualifiers(self) -> list[str]:
        """Hard NO conditions the AI applies even when keywords match (e.g. competitors)."""
        return self.niche.get("disqualifiers", [])

    @property
    def buyer_titles(self) -> list[str]:
        """Likely decision-maker titles, used to target contact-research links."""
        return self.niche.get("buyer_titles", [])

    @property
    def min_score(self) -> float:
        return float(self.raw.get("scoring", {}).get("min_score", 0.4))

    @property
    def max_enrich_per_run(self) -> int:
        """Hard ceiling on paid LLM calls per run — stops a loose config from
        triggering thousands of calls. Highest-scoring leads are enriched first."""
        return int(self.raw.get("scoring", {}).get("max_enrich_per_run", 40))

    @property
    def delivery(self) -> dict:
        return self.raw.get("delivery", {})

    @property
    def slack_webhook(self) -> str:
        """Resolve this client's Slack webhook so each client posts to its own channel.

        Precedence: explicit `webhook_url` > named `webhook_env` > legacy
        SLACK_WEBHOOK_URL env. Returns "" when delivery is disabled or unconfigured.
        Naming an env var (not the URL) keeps secrets out of committed configs.
        """
        slack = self.delivery.get("slack", {})
        if slack.get("enabled") is False:
            return ""
        if slack.get("webhook_url"):
            return str(slack["webhook_url"]).strip()
        if slack.get("webhook_env"):
            return os.getenv(slack["webhook_env"], "").strip()
        return os.getenv("SLACK_WEBHOOK_URL", "").strip()

    @property
    def sqlite_path(self) -> str:
        return self.raw.get("output", {}).get("sqlite_path", "leads.db")

    @property
    def csv_path(self) -> str:
        return self.raw.get("output", {}).get("csv_path", "leads.csv")


@dataclass
class LLMSettings:
    provider: str
    api_key: str
    model: str

    @classmethod
    def from_env(cls) -> "LLMSettings":
        return cls(
            provider=os.getenv("LLM_PROVIDER", "heuristic").strip().lower(),
            api_key=os.getenv("LLM_API_KEY", "").strip(),
            model=os.getenv("LLM_MODEL", "").strip(),
        )

from __future__ import annotations

import time

import requests

from .base import LLMClient

_MAX_RETRIES = 4


def _post_with_retry(url: str, headers: dict, payload: dict, timeout: int = 60):
    """POST with exponential backoff on 429 / 5xx — the free tiers rate-limit bursts.
    Returns the Response on success, or None if all retries are exhausted."""
    delay = 2.0
    for attempt in range(_MAX_RETRIES):
        try:
            resp = requests.post(url, headers=headers, json=payload, timeout=timeout)
        except requests.RequestException:
            if attempt == _MAX_RETRIES - 1:
                return None
            time.sleep(delay)
            delay *= 2
            continue
        if resp.status_code == 429 or resp.status_code >= 500:
            if attempt == _MAX_RETRIES - 1:
                return None
            retry_after = resp.headers.get("retry-after")
            time.sleep(float(retry_after) if retry_after else delay)
            delay *= 2
            continue
        return resp
    return None


class OpenAICompatClient(LLMClient):
    """Works for any OpenAI-compatible chat endpoint (Groq, OpenAI, Together, etc.)."""

    def __init__(self, base_url: str, api_key: str, model: str):
        self.base_url = base_url.rstrip("/")
        self.api_key = api_key
        self.model = model

    def complete(self, system: str, prompt: str, max_tokens: int = 600) -> str:
        resp = _post_with_retry(
            f"{self.base_url}/chat/completions",
            {"Authorization": f"Bearer {self.api_key}"},
            {
                "model": self.model,
                "max_tokens": max_tokens,
                "temperature": 0.6,
                "messages": [
                    {"role": "system", "content": system},
                    {"role": "user", "content": prompt},
                ],
            },
        )
        if resp is None:
            return ""
        try:
            resp.raise_for_status()
            return resp.json()["choices"][0]["message"]["content"].strip()
        except (requests.RequestException, KeyError, ValueError, IndexError):
            return ""


class AnthropicClient(LLMClient):
    def __init__(self, api_key: str, model: str):
        self.api_key = api_key
        self.model = model

    def complete(self, system: str, prompt: str, max_tokens: int = 600) -> str:
        resp = _post_with_retry(
            "https://api.anthropic.com/v1/messages",
            {
                "x-api-key": self.api_key,
                "anthropic-version": "2023-06-01",
                "content-type": "application/json",
            },
            {
                "model": self.model,
                "max_tokens": max_tokens,
                "system": system,
                "messages": [{"role": "user", "content": prompt}],
            },
        )
        if resp is None:
            return ""
        try:
            resp.raise_for_status()
            return resp.json()["content"][0]["text"].strip()
        except (requests.RequestException, KeyError, ValueError, IndexError):
            return ""


class HeuristicClient(LLMClient):
    """Zero-cost, zero-key fallback. Produces a usable (if generic) draft via templating
    so you can prove the full pipeline before spending a cent on inference."""

    def complete(self, system: str, prompt: str, max_tokens: int = 600) -> str:
        return (
            "FIT: MAYBE\n"
            "SUMMARY: [heuristic fallback — no LLM key set; qualify this lead manually]\n"
            "SUBJECT: Quick question about your post\n"
            "DRAFT: Saw your recent post and it lines up closely with what we help teams with. "
            "Worth a short conversation? "
            "(Set LLM_PROVIDER + LLM_API_KEY in .env for real personalised drafts.)"
        )

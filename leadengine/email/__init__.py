from __future__ import annotations

import os

from ..config import Config
from .base import EmailSender, NullSender
from .providers import DryRunSender, ResendSender

__all__ = [
    "EmailSender",
    "NullSender",
    "DryRunSender",
    "ResendSender",
    "build_email_sender",
]


def build_email_sender(config: Config) -> EmailSender:
    """Pick the transport from `delivery.email` config. Defaults to NullSender so the
    engine never sends unless a client explicitly opts in. The API key is read from a
    named env var, keeping secrets out of committed configs."""
    email = config.delivery.get("email", {})
    if not email.get("enabled"):
        return NullSender()

    provider = str(email.get("provider", "")).strip().lower()
    if provider == "dryrun":
        return DryRunSender()
    if provider == "resend":
        api_key = os.getenv(email.get("api_key_env", "RESEND_API_KEY"), "").strip()
        return ResendSender(
            api_key=api_key,
            from_addr=str(email.get("from_addr", "")).strip(),
            from_name=str(email.get("from_name", "")).strip(),
        )
    return NullSender()

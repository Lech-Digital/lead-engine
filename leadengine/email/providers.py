from __future__ import annotations

import requests

from .base import EmailSender

RESEND_API = "https://api.resend.com/emails"


class DryRunSender(EmailSender):
    """Pretends to send and records the message. Lets the whole approve->send flow be
    exercised at $0 with no external calls — the default until a real key is set."""

    enabled = True

    def __init__(self):
        self.sent: list[tuple[str, str, str]] = []

    def send(self, to: str, subject: str, body: str) -> bool:
        self.sent.append((to, subject, body))
        return True


class ResendSender(EmailSender):
    """Sends via Resend's transactional API (https://resend.com). One HTTP POST with a
    Bearer key. Postmark/SES can be added later behind the same EmailSender interface."""

    enabled = True

    def __init__(self, api_key: str, from_addr: str, from_name: str = ""):
        self.api_key = api_key
        self.from_addr = from_addr
        self.from_name = from_name

    def send(self, to: str, subject: str, body: str) -> bool:
        if not self.api_key or not self.from_addr:
            return False
        sender = f"{self.from_name} <{self.from_addr}>" if self.from_name else self.from_addr
        try:
            resp = requests.post(
                RESEND_API,
                headers={"Authorization": f"Bearer {self.api_key}"},
                json={"from": sender, "to": [to], "subject": subject, "text": body},
                timeout=20,
            )
            resp.raise_for_status()
            return True
        except requests.RequestException:
            return False  # never raise — caller treats False as "not sent, retry later"

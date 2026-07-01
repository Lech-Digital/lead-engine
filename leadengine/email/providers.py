from __future__ import annotations

import smtplib
from email.message import EmailMessage

import requests

from .base import EmailSender

RESEND_API = "https://api.resend.com/emails"


class DryRunSender(EmailSender):
    """Pretends to send and records the message. Lets the whole approve->send flow be
    exercised at $0 with no external calls — the default until a real key is set."""

    enabled = True

    def __init__(self):
        self.sent: list[tuple[str, str, str, str | None]] = []

    def send(self, to: str, subject: str, body: str, html: str | None = None) -> bool:
        self.sent.append((to, subject, body, html))
        return True


class ResendSender(EmailSender):
    """Sends via Resend's transactional API (https://resend.com). One HTTP POST with a
    Bearer key. Postmark/SES can be added later behind the same EmailSender interface."""

    enabled = True

    def __init__(self, api_key: str, from_addr: str, from_name: str = ""):
        self.api_key = api_key
        self.from_addr = from_addr
        self.from_name = from_name

    def send(self, to: str, subject: str, body: str, html: str | None = None) -> bool:
        if not self.api_key or not self.from_addr:
            return False
        sender = f"{self.from_name} <{self.from_addr}>" if self.from_name else self.from_addr
        payload = {"from": sender, "to": [to], "subject": subject, "text": body}
        if html:
            payload["html"] = html
        try:
            resp = requests.post(
                RESEND_API,
                headers={"Authorization": f"Bearer {self.api_key}"},
                json=payload,
                timeout=20,
            )
            resp.raise_for_status()
            return True
        except requests.RequestException:
            return False  # never raise — caller treats False as "not sent, retry later"


class GmailSmtpSender(EmailSender):
    """Sends via Gmail over SMTP-SSL, authenticating as `username` with a Google
    App Password (NOT the account password; requires 2-Step Verification). Gmail forces
    the From address to the authenticated account, so `from_addr` defaults to `username`.
    Slots behind the same EmailSender interface as ResendSender."""

    enabled = True
    HOST = "smtp.gmail.com"
    PORT = 465

    def __init__(self, username: str, password: str, from_name: str = ""):
        self.username = username
        self.password = password
        self.from_name = from_name

    def send(self, to: str, subject: str, body: str, html: str | None = None) -> bool:
        if not self.username or not self.password:
            return False
        msg = EmailMessage()
        msg["From"] = f"{self.from_name} <{self.username}>" if self.from_name else self.username
        msg["To"] = to
        msg["Subject"] = subject
        msg.set_content(body)
        if html:
            msg.add_alternative(html, subtype="html")  # multipart/alternative: text + HTML
        try:
            with smtplib.SMTP_SSL(self.HOST, self.PORT) as smtp:
                smtp.login(self.username, self.password)
                smtp.send_message(msg)
            return True
        except OSError:
            return False  # never raise — caller treats False as "not sent, retry later"

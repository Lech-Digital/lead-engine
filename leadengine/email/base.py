from __future__ import annotations

from abc import ABC, abstractmethod


class EmailSender(ABC):
    """An outbound email transport. Like LLMClient, send() must NEVER raise — it
    returns False on any failure so one bad send doesn't abort a batch."""

    enabled: bool = False

    @abstractmethod
    def send(self, to: str, subject: str, body: str) -> bool:
        raise NotImplementedError


class NullSender(EmailSender):
    """No email configured. Always disabled; sending is a no-op that returns False."""

    enabled = False

    def send(self, to: str, subject: str, body: str) -> bool:
        return False

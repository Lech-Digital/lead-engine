from __future__ import annotations

from abc import ABC, abstractmethod


class LLMClient(ABC):
    @abstractmethod
    def complete(self, system: str, prompt: str, max_tokens: int = 600) -> str:
        """Return the model's text response. Must never raise — return '' on failure
        so one bad lead does not abort the batch."""
        raise NotImplementedError

from ..config import LLMSettings
from .base import LLMClient
from .providers import AnthropicClient, HeuristicClient, OpenAICompatClient

__all__ = ["LLMClient", "build_llm"]


def build_llm(settings: LLMSettings) -> LLMClient:
    """Pick a provider. Falls back to the free heuristic client when no key is set,
    so the whole pipeline runs end-to-end on $0."""
    provider = settings.provider
    if not settings.api_key or provider == "heuristic":
        return HeuristicClient()
    if provider == "groq":
        return OpenAICompatClient(
            base_url="https://api.groq.com/openai/v1",
            api_key=settings.api_key,
            model=settings.model or "llama-3.3-70b-versatile",
        )
    if provider == "openai":
        return OpenAICompatClient(
            base_url="https://api.openai.com/v1",
            api_key=settings.api_key,
            model=settings.model or "gpt-4o-mini",
        )
    if provider == "anthropic":
        return AnthropicClient(
            api_key=settings.api_key,
            model=settings.model or "claude-haiku-4-5-20251001",
        )
    return HeuristicClient()

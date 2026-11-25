"""LLM provider abstraction: Ollama (local), OpenAI and Gemini (cloud), chosen by LLM_PROVIDER.

All three speak plain HTTP through httpx so the transport can be replaced in tests. Nothing here is
verified against the live services in this repository's tests (see README); the request shapes follow
each vendor's public API documentation.
"""
from __future__ import annotations

import base64
from abc import ABC, abstractmethod

import httpx

from app.config import Settings


class ProviderError(Exception):
    """The configured provider is unreachable, unconfigured or returned an error."""


class LLMProvider(ABC):
    name: str

    @abstractmethod
    async def chat(self, system: str, user: str, *, json_mode: bool = False) -> str: ...

    @abstractmethod
    async def embed(self, texts: list[str]) -> list[list[float]]: ...

    async def tts(self, text: str) -> tuple[bytes, str] | None:
        """Text to speech hook. None when this provider has no TTS configured."""
        return None


def _need(value: str, what: str) -> str:
    if not value:
        raise ProviderError(f"{what} is not configured.")
    return value


async def _post(client: httpx.AsyncClient, url: str, **kw) -> httpx.Response:
    try:
        r = await client.post(url, **kw)
    except httpx.HTTPError as ex:
        raise ProviderError(f"Provider unreachable: {type(ex).__name__}") from ex
    if r.status_code >= 400:
        raise ProviderError(f"Provider returned HTTP {r.status_code}.")
    return r


class OllamaProvider(LLMProvider):
    name = "ollama"

    def __init__(self, s: Settings, transport: httpx.AsyncBaseTransport | None = None):
        self.s, self.transport = s, transport

    def _client(self) -> httpx.AsyncClient:
        return httpx.AsyncClient(base_url=self.s.ollama_base_url, timeout=self.s.llm_timeout_seconds, transport=self.transport)

    async def chat(self, system: str, user: str, *, json_mode: bool = False) -> str:
        body = {"model": self.s.ollama_chat_model, "stream": False, "messages": [{"role": "system", "content": system}, {"role": "user", "content": user}]}
        if json_mode:
            body["format"] = "json"
        async with self._client() as c:
            return (await _post(c, "/api/chat", json=body)).json()["message"]["content"]

    async def embed(self, texts: list[str]) -> list[list[float]]:
        async with self._client() as c:
            return (await _post(c, "/api/embed", json={"model": self.s.ollama_embed_model, "input": texts})).json()["embeddings"]


class OpenAIProvider(LLMProvider):
    name = "openai"

    def __init__(self, s: Settings, transport: httpx.AsyncBaseTransport | None = None):
        self.s, self.transport = s, transport

    def _client(self) -> httpx.AsyncClient:
        key = _need(self.s.openai_api_key, "OPENAI_API_KEY")
        return httpx.AsyncClient(base_url=self.s.openai_base_url, headers={"Authorization": f"Bearer {key}"}, timeout=self.s.llm_timeout_seconds, transport=self.transport)

    async def chat(self, system: str, user: str, *, json_mode: bool = False) -> str:
        body = {"model": _need(self.s.openai_chat_model, "OPENAI_CHAT_MODEL"), "messages": [{"role": "system", "content": system}, {"role": "user", "content": user}]}
        if json_mode:
            body["response_format"] = {"type": "json_object"}
        async with self._client() as c:
            return (await _post(c, "/chat/completions", json=body)).json()["choices"][0]["message"]["content"]

    async def embed(self, texts: list[str]) -> list[list[float]]:
        async with self._client() as c:
            r = (await _post(c, "/embeddings", json={"model": _need(self.s.openai_embed_model, "OPENAI_EMBED_MODEL"), "input": texts})).json()
        return [d["embedding"] for d in sorted(r["data"], key=lambda d: d["index"])]

    async def tts(self, text: str) -> tuple[bytes, str] | None:
        if not self.s.openai_tts_model:
            return None
        async with self._client() as c:
            r = await _post(c, "/audio/speech", json={"model": self.s.openai_tts_model, "voice": self.s.openai_tts_voice, "input": text[:4000], "response_format": "mp3"})
        return r.content, "audio/mpeg"


class GeminiProvider(LLMProvider):
    name = "gemini"

    def __init__(self, s: Settings, transport: httpx.AsyncBaseTransport | None = None):
        self.s, self.transport = s, transport

    def _client(self) -> httpx.AsyncClient:
        key = _need(self.s.gemini_api_key, "GEMINI_API_KEY")
        return httpx.AsyncClient(base_url=self.s.gemini_base_url, headers={"x-goog-api-key": key}, timeout=self.s.llm_timeout_seconds, transport=self.transport)

    async def chat(self, system: str, user: str, *, json_mode: bool = False) -> str:
        model = _need(self.s.gemini_chat_model, "GEMINI_CHAT_MODEL")
        body: dict = {"systemInstruction": {"parts": [{"text": system}]}, "contents": [{"role": "user", "parts": [{"text": user}]}]}
        if json_mode:
            body["generationConfig"] = {"responseMimeType": "application/json"}
        async with self._client() as c:
            r = (await _post(c, f"/models/{model}:generateContent", json=body)).json()
        try:
            return "".join(p.get("text", "") for p in r["candidates"][0]["content"]["parts"])
        except (KeyError, IndexError) as ex:
            raise ProviderError("Provider returned no answer.") from ex

    async def embed(self, texts: list[str]) -> list[list[float]]:
        model = _need(self.s.gemini_embed_model, "GEMINI_EMBED_MODEL")
        reqs = [{"model": f"models/{model}", "content": {"parts": [{"text": t}]}} for t in texts]
        async with self._client() as c:
            r = (await _post(c, f"/models/{model}:batchEmbedContents", json={"requests": reqs})).json()
        return [e["values"] for e in r["embeddings"]]


def create_provider(s: Settings, transport: httpx.AsyncBaseTransport | None = None) -> LLMProvider:
    """Provider factory. Switch with LLM_PROVIDER=ollama|openai|gemini in .env."""
    return {"ollama": OllamaProvider, "openai": OpenAIProvider, "gemini": GeminiProvider}[s.llm_provider](s, transport)


def b64(data: bytes) -> str:
    return base64.b64encode(data).decode()

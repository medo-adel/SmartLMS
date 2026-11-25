from functools import lru_cache

from app.config import get_settings
from app.llm import create_provider
from app.media import WhisperTranscriber
from app.services import Services
from app.store import VectorStore


@lru_cache
def get_services() -> Services:
    s = get_settings()
    return Services(s, create_provider(s), VectorStore.persistent(s.chroma_persist_dir), WhisperTranscriber(s))

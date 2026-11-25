from functools import lru_cache
from typing import Literal

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    ai_service_key: str = ""
    ai_host: str = "127.0.0.1"
    ai_port: int = 8001

    llm_provider: Literal["ollama", "openai", "gemini"] = "ollama"
    llm_timeout_seconds: float = 120
    ollama_base_url: str = "http://localhost:11434"
    ollama_chat_model: str = "llama3.1:8b"
    ollama_embed_model: str = "nomic-embed-text"

    openai_api_key: str = ""
    openai_base_url: str = "https://api.openai.com/v1"
    openai_chat_model: str = ""
    openai_embed_model: str = ""
    openai_tts_model: str = ""
    openai_tts_voice: str = "alloy"
    gemini_api_key: str = ""
    gemini_base_url: str = "https://generativelanguage.googleapis.com/v1beta"
    gemini_chat_model: str = ""
    gemini_embed_model: str = ""

    chroma_persist_dir: str = "./data/chroma"
    # Files to ingest must live under this directory (the .NET API's file storage root). Anything else is refused.
    media_root: str = ""

    whisper_model: str = "small"
    whisper_device: str = "cpu"
    whisper_compute_type: str = "int8"

    # Retrieval and plagiarism thresholds
    retrieval_top_k: int = 5
    min_retrieval_similarity: float = 0.3
    plagiarism_flag_threshold: float = 0.8
    max_upload_mb: int = 25


@lru_cache
def get_settings() -> Settings:
    return Settings()

"""Reads course files and turns them into text with locations. Only files under MEDIA_ROOT can be read."""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Protocol

from app.config import Settings


class SourceRefused(ValueError):
    pass


@dataclass
class Segment:
    text: str
    page: int | None = None
    start: float | None = None
    end: float | None = None


class Transcriber(Protocol):
    def transcribe(self, path: Path, language: str | None) -> list[Segment]: ...


class WhisperTranscriber:
    """Faster-Whisper, loaded on first use. Downloads the model on first run unless it is already cached."""

    def __init__(self, s: Settings):
        self.s, self._model = s, None

    def transcribe(self, path: Path, language: str | None) -> list[Segment]:
        if self._model is None:
            from faster_whisper import WhisperModel
            self._model = WhisperModel(self.s.whisper_model, device=self.s.whisper_device, compute_type=self.s.whisper_compute_type)
        segs, _ = self._model.transcribe(str(path), language=language, vad_filter=True)
        return [Segment(text=s.text.strip(), start=float(s.start), end=float(s.end)) for s in segs if s.text.strip()]


def resolve_source(s: Settings, uri: str) -> Path:
    if not s.media_root:
        raise SourceRefused("MEDIA_ROOT is not configured, so no file can be read.")
    if "://" in uri:
        raise SourceRefused("Only files under the media root are accepted, not URLs.")
    root = Path(s.media_root).resolve()
    p = (root / uri).resolve() if not Path(uri).is_absolute() else Path(uri).resolve()
    if root != p and root not in p.parents:
        raise SourceRefused("The file is outside the media root.")
    if not p.is_file():
        raise SourceRefused("The file does not exist.")
    return p


def read_pdf(path: Path) -> list[Segment]:
    import pymupdf as fitz
    out = []
    with fitz.open(path) as doc:
        for i, page in enumerate(doc, start=1):
            t = page.get_text().strip()
            if t:
                out.append(Segment(text=t, page=i))
    return out


def read_source(s: Settings, source_type: str, uri: str, text: str | None, language: str | None, transcriber: Transcriber) -> list[Segment]:
    if source_type == "text":
        if text is not None:
            return [Segment(text=text)]
        return [Segment(text=resolve_source(s, uri).read_text(encoding="utf-8", errors="replace"))]
    p = resolve_source(s, uri)
    if source_type == "pdf":
        return read_pdf(p)
    return transcriber.transcribe(p, language)

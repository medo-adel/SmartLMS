import math
import re

_WS = re.compile(r"\s+")


def chunk_text(text: str, size: int = 900, overlap: int = 150) -> list[str]:
    """Split on sentence-ish boundaries into ~size character chunks with a small overlap."""
    text = _WS.sub(" ", text).strip()
    if not text:
        return []
    sentences = re.split(r"(?<=[.!?؟。])\s+", text)
    chunks, cur = [], ""
    for s in sentences:
        while len(s) > size:                       # one very long sentence: hard split
            if cur:
                chunks.append(cur); cur = ""
            chunks.append(s[:size]); s = s[size - overlap:]
        if len(cur) + len(s) + 1 > size and cur:
            chunks.append(cur)
            cur = cur[-overlap:] + " " + s if overlap else s
        else:
            cur = (cur + " " + s).strip()
    if cur:
        chunks.append(cur)
    return chunks


def shingles(text: str, n: int = 5) -> set[str]:
    words = re.findall(r"\w+", text.lower())
    return {" ".join(words[i:i + n]) for i in range(max(0, len(words) - n + 1))} or set(words)


def jaccard(a: set[str], b: set[str]) -> float:
    return len(a & b) / len(a | b) if a and b else 0.0


def cosine(a: list[float], b: list[float]) -> float:
    na, nb = math.sqrt(sum(x * x for x in a)), math.sqrt(sum(x * x for x in b))
    return sum(x * y for x, y in zip(a, b)) / (na * nb) if na and nb else 0.0

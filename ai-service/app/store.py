"""ChromaDB persistence. Every query is filtered by tenant_id AND course_id (or assignment_id): there is no unfiltered read."""
from __future__ import annotations

from dataclasses import dataclass

import chromadb

CHUNKS = "course_chunks"
ASSIGNMENTS = "assignment_submissions"


@dataclass
class Hit:
    id: str
    text: str
    meta: dict
    similarity: float


class VectorStore:
    def __init__(self, client: chromadb.ClientAPI):
        self.client = client
        self.chunks = client.get_or_create_collection(CHUNKS, metadata={"hnsw:space": "cosine"}, embedding_function=None)
        self.subs = client.get_or_create_collection(ASSIGNMENTS, metadata={"hnsw:space": "cosine"}, embedding_function=None)

    @classmethod
    def persistent(cls, path: str) -> "VectorStore":
        # Telemetry off: the service makes no outbound calls of its own at startup or during ingest.
        return cls(chromadb.PersistentClient(path=path, settings=chromadb.Settings(anonymized_telemetry=False)))

    @staticmethod
    def _scope(tenant_id: str, **kw: str) -> dict:
        conds = [{"tenant_id": tenant_id}] + [{k: v} for k, v in kw.items()]
        return {"$and": conds}

    # ---- course material -------------------------------------------------------------------------------------------
    def replace_lesson(self, tenant_id: str, course_id: str, lesson_id: str, items: list[tuple[str, list[float], dict]]) -> int:
        """Idempotent re-index: old chunks of the lesson are removed first. items = (text, embedding, meta)."""
        self.chunks.delete(where=self._scope(tenant_id, course_id=course_id, lesson_id=lesson_id))
        if not items:
            return 0
        ids = [f"{tenant_id}:{lesson_id}:{i}" for i in range(len(items))]
        metas = [{**m, "tenant_id": tenant_id, "course_id": course_id, "lesson_id": lesson_id, "ord": i} for i, (_, _, m) in enumerate(items)]
        self.chunks.add(ids=ids, documents=[t for t, _, _ in items], embeddings=[e for _, e, _ in items], metadatas=metas)
        return len(items)

    def search(self, tenant_id: str, course_id: str, embedding: list[float], k: int, *, sources: list[str] | None = None) -> list[Hit]:
        where = self._scope(tenant_id, course_id=course_id)
        if sources:
            where["$and"].append({"source": {"$in": sources}})
        if self.chunks.count() == 0:
            return []
        r = self.chunks.query(query_embeddings=[embedding], n_results=k, where=where)
        return [Hit(i, d, m, 1.0 - dist) for i, d, m, dist in zip(r["ids"][0], r["documents"][0], r["metadatas"][0], r["distances"][0])]

    def lesson_chunks(self, tenant_id: str, course_id: str, lesson_id: str) -> list[Hit]:
        r = self.chunks.get(where=self._scope(tenant_id, course_id=course_id, lesson_id=lesson_id))
        hits = [Hit(i, d, m, 1.0) for i, d, m in zip(r["ids"], r["documents"], r["metadatas"])]
        return sorted(hits, key=lambda h: h.meta.get("ord", 0))

    # ---- assignment submissions (plagiarism) ----------------------------------------------------------------------
    def upsert_submission(self, tenant_id: str, course_id: str, assignment_id: str, submission_id: str, text: str, embedding: list[float]) -> None:
        self.subs.upsert(ids=[f"{tenant_id}:{assignment_id}:{submission_id}"], documents=[text], embeddings=[embedding],
                         metadatas=[{"tenant_id": tenant_id, "course_id": course_id, "assignment_id": assignment_id, "submission_id": submission_id}])

    def other_submissions(self, tenant_id: str, assignment_id: str, submission_id: str) -> list[tuple[str, str, list[float]]]:
        r = self.subs.get(where=self._scope(tenant_id, assignment_id=assignment_id), include=["documents", "embeddings", "metadatas"])
        return [(m["submission_id"], d, list(e)) for m, d, e in zip(r["metadatas"], r["documents"], r["embeddings"]) if m["submission_id"] != submission_id]

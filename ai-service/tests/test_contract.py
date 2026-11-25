from fastapi.testclient import TestClient

from app.config import Settings, get_settings
from app.main import app

client = TestClient(app)
app.dependency_overrides[get_settings] = lambda: Settings(ai_service_key="test-key", _env_file=None)

PATHS = [
    "/ai/ingest", "/ai/tutor/chat", "/ai/video/semantic-search", "/ai/generate-quiz", "/ai/summarize",
    "/ai/mindmap", "/ai/grade-essay", "/ai/plagiarism-check", "/ai/voice-tutor", "/ai/study-plan",
    "/ai/predict-risk",
]


def test_health_open():
    assert client.get("/health").status_code == 200


def test_all_eleven_endpoints_in_openapi():
    paths = client.get("/openapi.json").json()["paths"]
    for p in PATHS:
        assert p in paths


def test_missing_key_rejected():
    assert client.post("/ai/summarize", json={"tenant_id": "t", "course_id": "c", "lesson_id": "l"}).status_code == 401


def test_wrong_key_rejected():
    r = client.post("/ai/summarize", headers={"X-AI-Service-Key": "nope"},
                    json={"tenant_id": "t", "course_id": "c", "lesson_id": "l"})
    assert r.status_code == 401


def test_empty_server_key_fails_closed():
    app.dependency_overrides[get_settings] = lambda: Settings(ai_service_key="", _env_file=None)
    r = client.post("/ai/summarize", headers={"X-AI-Service-Key": ""},
                    json={"tenant_id": "t", "course_id": "c", "lesson_id": "l"})
    assert r.status_code == 401
    app.dependency_overrides[get_settings] = lambda: Settings(ai_service_key="test-key", _env_file=None)

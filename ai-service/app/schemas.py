"""Request and response contracts for the 11 AI endpoints (Pydantic v2).

These are the agreed contract for Phase 5. The .NET IAiServiceClient mirrors them.
All requests carry tenant_id and course_id so the service can partition vectors.
"""
from typing import Literal

from pydantic import BaseModel, Field


class Scope(BaseModel):
    tenant_id: str
    course_id: str


class Citation(BaseModel):
    lesson_id: str
    page: int | None = None
    start_seconds: float | None = None
    end_seconds: float | None = None
    snippet: str


# 1. POST /ai/ingest
class IngestRequest(Scope):
    lesson_id: str
    source_type: Literal["pdf", "video", "audio", "text"]
    source_uri: str = Field(description="Path or URL the AI service is allowed to read.")
    language: str | None = None
    text: str | None = Field(default=None, description="Lesson text sent inline for source_type=text; no file is read then.")


class TranscriptCue(BaseModel):
    start: float
    end: float
    text: str


class IngestResponse(BaseModel):
    lesson_id: str
    chunks_indexed: int
    transcript_segments: int = 0
    cues: list[TranscriptCue] = []  # timed Whisper cues, only for video/audio sources


# 2. POST /ai/tutor/chat
class TutorChatRequest(Scope):
    question: str = Field(min_length=1, max_length=4000)
    history: list[dict[str, str]] = Field(default_factory=list)
    language: str | None = None


class TutorChatResponse(BaseModel):
    answer: str
    citations: list[Citation]
    grounded: bool = Field(description="False when no course material matched; the answer then says so.")


# 3. POST /ai/video/semantic-search
class SemanticSearchRequest(Scope):
    query: str = Field(min_length=1, max_length=500)
    top_k: int = Field(default=5, ge=1, le=20)


class SemanticSearchHit(BaseModel):
    lesson_id: str
    start_seconds: float
    end_seconds: float
    snippet: str
    score: float


class SemanticSearchResponse(BaseModel):
    hits: list[SemanticSearchHit]


# 4. POST /ai/generate-quiz
class GenerateQuizRequest(Scope):
    lesson_id: str
    mcq_count: int = Field(default=5, ge=0, le=30)
    open_ended_count: int = Field(default=2, ge=0, le=10)
    flashcard_count: int = Field(default=5, ge=0, le=30)
    difficulty: Literal["easy", "medium", "hard"] = "medium"


class GeneratedMcq(BaseModel):
    question: str
    options: list[str]
    correct_index: int
    explanation: str


class GeneratedOpenQuestion(BaseModel):
    question: str
    model_answer: str


class Flashcard(BaseModel):
    front: str
    back: str


class GenerateQuizResponse(BaseModel):
    mcqs: list[GeneratedMcq]
    open_ended: list[GeneratedOpenQuestion]
    flashcards: list[Flashcard]


# 5. POST /ai/summarize
class SummarizeRequest(Scope):
    lesson_id: str


class ChapterMarker(BaseModel):
    title: str
    start_seconds: float


class SummarizeResponse(BaseModel):
    takeaways: list[str]
    key_terms: list[str]
    chapters: list[ChapterMarker]


# 6. POST /ai/mindmap
class MindMapRequest(Scope):
    lesson_id: str


class MindMapNode(BaseModel):
    id: str
    label: str


class MindMapEdge(BaseModel):
    source: str
    target: str


class MindMapResponse(BaseModel):
    nodes: list[MindMapNode]
    edges: list[MindMapEdge]


# 7. POST /ai/grade-essay
class RubricCriterion(BaseModel):
    name: str
    max_points: float
    description: str


class GradeEssayRequest(Scope):
    assignment_id: str
    essay_text: str = Field(min_length=1, max_length=40000)
    rubric: list[RubricCriterion] = Field(min_length=1)


class CriterionScore(BaseModel):
    name: str
    points: float
    feedback: str


class GradeEssayResponse(BaseModel):
    provisional: Literal[True] = True
    total_points: float
    max_points: float
    criteria: list[CriterionScore]
    feedback: str


# 8. POST /ai/plagiarism-check
class PlagiarismCheckRequest(Scope):
    assignment_id: str
    submission_id: str
    text: str = Field(min_length=1, max_length=200000)


class PlagiarismMatch(BaseModel):
    other_submission_id: str
    similarity: float = Field(ge=0, le=1)


class PlagiarismCheckResponse(BaseModel):
    max_similarity: float
    flagged: list[PlagiarismMatch]
    note: str = "Similarity score only. It is not proof of plagiarism; a person must review flagged matches."


# 9. POST /ai/voice-tutor (multipart: audio file + JSON fields)
class VoiceTutorResponse(BaseModel):
    transcript: str
    answer_text: str
    audio_base64: str | None = Field(default=None, description="Spoken answer. Null when the provider has no TTS configured.")
    audio_mime: str | None = None
    citations: list[Citation]


# 10. POST /ai/study-plan
class StudyPlanLesson(BaseModel):
    title: str
    hours: float = Field(gt=0)


class StudyPlanRequest(Scope):
    lessons: list[StudyPlanLesson] = Field(default_factory=list, description="Optional ordered lessons to schedule; otherwise a generic block label is used.")
    total_lesson_hours: float = Field(gt=0)
    hours_per_week_available: float = Field(gt=0)
    target_date: str = Field(description="ISO date, yyyy-mm-dd")
    start_date: str = Field(description="ISO date, yyyy-mm-dd")


class StudyPlanDay(BaseModel):
    date: str
    focus: str
    minutes: int


class StudyPlanResponse(BaseModel):
    days: list[StudyPlanDay]
    feasible: bool
    note: str | None = None


# 11. POST /ai/predict-risk
class RiskFeatures(BaseModel):
    days_since_last_login: int = Field(ge=0)
    lessons_completed_ratio: float = Field(ge=0, le=1)
    average_quiz_score: float | None = Field(default=None, ge=0, le=100)
    weekly_study_minutes: float = Field(ge=0)
    overdue_assignments: int = Field(ge=0)


class PredictRiskRequest(Scope):
    student_id: str
    features: RiskFeatures


class PredictRiskResponse(BaseModel):
    risk_score: float = Field(ge=0, le=1)
    level: Literal["low", "medium", "high"]
    interventions: list[str]
    note: str = "Advisory estimate from engagement data. Do not use it alone for decisions about a student."

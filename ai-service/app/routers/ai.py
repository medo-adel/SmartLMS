import os
from pathlib import Path
from typing import Annotated

from fastapi import APIRouter, Depends, File, Form, HTTPException, UploadFile

from app import schemas as S
from app import services as svc
from app.deps import get_services
from app.llm import ProviderError
from app.media import SourceRefused
from app.security import require_service_key

router = APIRouter(prefix="/ai", tags=["ai"], dependencies=[Depends(require_service_key)])
Sv = Annotated[svc.Services, Depends(get_services)]


async def _run(coro):
    """Maps domain failures to honest HTTP errors. Detail text never contains course text or keys."""
    try:
        return await coro
    except svc.NotIndexed as ex:
        raise HTTPException(409, str(ex))
    except SourceRefused as ex:
        raise HTTPException(422, str(ex))
    except (svc.BadRequest, ValueError) as ex:
        raise HTTPException(422, str(ex))
    except svc.BadModelOutput:
        raise HTTPException(502, "The model returned output that could not be validated. Try again.")
    except ProviderError as ex:
        raise HTTPException(503, str(ex))
    except ImportError as ex:
        raise HTTPException(503, f"A required component is not installed: {ex.name}")


@router.post("/ingest", response_model=S.IngestResponse)
async def ingest(body: S.IngestRequest, sv: Sv):
    return await _run(svc.ingest(sv, body))


@router.post("/tutor/chat", response_model=S.TutorChatResponse)
async def tutor_chat(body: S.TutorChatRequest, sv: Sv):
    return await _run(svc.tutor_chat(sv, body))


@router.post("/video/semantic-search", response_model=S.SemanticSearchResponse)
async def semantic_search(body: S.SemanticSearchRequest, sv: Sv):
    return await _run(svc.semantic_search(sv, body))


@router.post("/generate-quiz", response_model=S.GenerateQuizResponse)
async def generate_quiz(body: S.GenerateQuizRequest, sv: Sv):
    return await _run(svc.generate_quiz(sv, body))


@router.post("/summarize", response_model=S.SummarizeResponse)
async def summarize(body: S.SummarizeRequest, sv: Sv):
    return await _run(svc.summarize(sv, body))


@router.post("/mindmap", response_model=S.MindMapResponse)
async def mindmap(body: S.MindMapRequest, sv: Sv):
    return await _run(svc.mindmap(sv, body))


@router.post("/grade-essay", response_model=S.GradeEssayResponse)
async def grade_essay(body: S.GradeEssayRequest, sv: Sv):
    return await _run(svc.grade_essay(sv, body))


@router.post("/plagiarism-check", response_model=S.PlagiarismCheckResponse)
async def plagiarism_check(body: S.PlagiarismCheckRequest, sv: Sv):
    return await _run(svc.plagiarism_check(sv, body))


@router.post("/voice-tutor", response_model=S.VoiceTutorResponse)
async def voice_tutor(sv: Sv, audio: UploadFile = File(...), tenant_id: str = Form(...), course_id: str = Form(...), language: str | None = Form(None)):
    limit = sv.settings.max_upload_mb * 1024 * 1024
    data = await audio.read(limit + 1)
    if len(data) > limit:
        raise HTTPException(413, "The audio file is too large.")
    suffix = os.path.splitext(audio.filename or "")[1]
    return await _run(svc.voice_tutor(sv, tenant_id, course_id, data, suffix, language))


@router.post("/study-plan", response_model=S.StudyPlanResponse)
async def study_plan(body: S.StudyPlanRequest):
    try:
        return svc.study_plan(body)
    except svc.BadRequest as ex:
        raise HTTPException(422, str(ex))


@router.post("/predict-risk", response_model=S.PredictRiskResponse)
async def predict_risk(body: S.PredictRiskRequest):
    return svc.predict_risk(body)

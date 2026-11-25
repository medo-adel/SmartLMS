# Phase 5: Python AI service and .NET integration

Scope (original specification, Phase 5): the FastAPI service with ChromaDB, a configurable LLM provider (Ollama, OpenAI, Gemini), Whisper transcription and the 11 AI endpoints; the typed `IAiServiceClient`, the .NET AI controller and Hangfire jobs for ingestion and plagiarism checks. Phase 6 is not started.
Authorisation: the user's Phase 5 go-ahead, as relayed by the main agent. Phase 4 decisions are unchanged.

## How a request flows

Ionic app -> .NET API (`/api/v1/ai/...`) -> `IAiServiceClient` -> Python service (`/ai/...`, header `X-AI-Service-Key`).
The app never talks to Python. The .NET API checks, in order: JWT role, tenant (EF query filters), device binding (existing middleware), course access (enrolled student or course staff, otherwise 404), a per-user rate limit (`AiService:RequestsPerMinute`, default 20, counted from `AiUsageLogs`, answer 429), then forwards. Every call writes an `AiUsageLogs` row (endpoint, course, latency, status; no prompt or answer text). Generating actions also write an `AuditLogs` row (who, what, which object; no content).

## Endpoints (.NET) and what they do

| Route | Roles | Notes |
|---|---|---|
| POST `/ai/lessons/{id}/ingest` | TenantAdmin, Instructor (course author) | 202 + job id. Hangfire job `AiIngest`, updates `LessonIndexStates`, SignalR event `ai-job`, stored notification. |
| GET `/ai/lessons/{id}/index-status` | same | Index state and the latest job. |
| POST `/ai/courses/{id}/tutor/chat` | Student (enrolled), Instructor | Answers only from that course. No match above the threshold: fixed "not in the course material" answer and the model is not called. |
| POST `/ai/courses/{id}/video/semantic-search` | Student, Instructor | Timestamps and snippets from video/audio transcripts; hits from unpublished lessons are filtered out for students. |
| POST `/ai/lessons/{id}/generate-quiz` | TenantAdmin, Instructor, TA | Returns a draft. Nothing is saved to the question bank. |
| POST `/ai/lessons/{id}/summarize`, `/mindmap` | Student, Instructor | Student needs a published lesson. |
| POST `/ai/submissions/{id}/grade-essay` | TenantAdmin, Instructor, TA | Only if the assignment has `AiProvisionalGradingEnabled` and a rubric. Writes only `AiProvisionalScore` and `AiFeedback`. `TotalScore`, status and criterion scores are never touched. |
| POST `/ai/assignments/{id}/plagiarism-check` | TenantAdmin, Instructor (author) | Only if `PlagiarismCheckEnabled`. 202 + job id. Stores `PlagiarismChecks` and `PlagiarismMatches` (outcome Unreviewed) and `Submissions.MaxSimilarity`. |
| GET `/ai/submissions/{id}/plagiarism` | same | Scores for human review. |
| POST `/ai/courses/{id}/voice-tutor` | Student | Multipart audio (25 MB, audio extensions only). Text answer always; audio only if TTS is configured. |
| POST `/ai/courses/{id}/study-plan` | Student | Body: `targetDate`, `hoursPerWeek`. Built from the student's unfinished published lessons. |
| GET `/ai/students/{id}/risk?courseId=` | TenantAdmin, Instructor (author) | Advisory. Features are computed from the database and saved to `DropoutRiskScores`. |

Assignment create/update now accept optional `plagiarismCheckEnabled` and `aiProvisionalGradingEnabled` (omitted means unchanged).

## Python service

- `app/llm.py`: provider interface and factory (`LLM_PROVIDER`), Ollama/OpenAI/Gemini over plain HTTP (httpx). OpenAI TTS only when `OPENAI_TTS_MODEL` is set.
- `app/store.py`: ChromaDB persistent store. Every query filters on `tenant_id` and `course_id` (assignment id for plagiarism). Re-ingest replaces the lesson's chunks.
- `app/media.py`: PDF via PyMuPDF (page numbers kept), audio/video via Faster-Whisper segments (timestamps kept). Files must be under `MEDIA_ROOT`; URLs, `..` and absolute paths outside it are refused.
- `app/services.py`: the logic. Model output is validated with Pydantic and retried once; the course text is marked as untrusted data in prompts. Essay grading: the server clamps each criterion to its maximum and computes the total itself, and the answer is always `provisional`. Plagiarism: shared 5-word runs (containment) or near-identical embeddings, flagged at 0.8; the answer says it is not proof. Study plan and risk are deterministic formulas, no model.
- Errors map to 409 (not indexed), 422 (bad input/source), 502 (model output invalid), 503 (provider down or unconfigured). Error text never contains course text or keys.

## Run

See the README ("Run the AI service"). Key settings: `AI_SERVICE_KEY` (same value as `AiService:ApiKey` in user-secrets), `MEDIA_ROOT` (same folder as `Storage:Root`), `LLM_PROVIDER`. Tests: `cd ai-service && pytest`; `cd backend && dotnet test`.

## Limits, stated plainly

- No live Ollama, OpenAI or Gemini call was made. Provider code is tested against mocked HTTP only; request shapes follow the vendors' public docs and may need adjusting against the real services.
- Faster-Whisper transcription and the model download were not run. Tests use a fake transcriber. The first real run downloads the model (`small` is about 460 MB).
- ChromaDB was run for real (local, in-process), with a hash-based fake embedder, so retrieval quality with real embeddings is unmeasured.
- OpenAI/Gemini send course text to a third party. Ollama stays on the machine.
- TTS exists only through OpenAI. With Ollama or Gemini the voice tutor returns text and `audio_base64` is null.
- Python and .NET must see the lesson files at the same path (`MEDIA_ROOT` = `Storage:Root`). If they run on different machines, ingestion of PDF/audio/video will fail with a clear error. Article lessons are sent inline and work anywhere.
- Plagiarism checks cover text submissions only (file submissions are not built). A plagiarism batch makes two calls per submission.
- Risk features: "weekly study minutes" uses `WatchedSeconds` of lessons opened in the last 7 days, a rough proxy. Risk and study plan formulas are simple and not validated against real outcomes.
- Lessons with no stored duration count as 15 minutes in study plans (the response says so).
- The rate limit counts calls in SQL, so it is per user across servers but approximate under heavy parallel bursts.
- Not run on SQL Server or Windows. No new migration was needed: `AiIngest` and `PlagiarismBatch` job types and all tables already existed.

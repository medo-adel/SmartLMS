from fastapi import FastAPI

from app.routers import ai

app = FastAPI(
    title="SmartLMS AI Service",
    version="0.5.0",
    description="Internal service. Only the SmartLMS .NET API may call it (X-AI-Service-Key).",
)
app.include_router(ai.router)


@app.get("/health", tags=["health"])
async def health() -> dict[str, str]:
    return {"status": "Healthy", "service": "smartlms-ai"}

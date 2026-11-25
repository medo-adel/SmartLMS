import secrets
from typing import Annotated

from fastapi import Depends, Header, HTTPException, status

from app.config import Settings, get_settings


def require_service_key(
    settings: Annotated[Settings, Depends(get_settings)],
    x_ai_service_key: Annotated[str | None, Header()] = None,
) -> None:
    """Only the .NET API may call this service. Fails closed when no key is configured."""
    expected = settings.ai_service_key
    if not expected or not x_ai_service_key or not secrets.compare_digest(
        x_ai_service_key.encode(), expected.encode()
    ):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid or missing service key.")

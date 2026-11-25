# SmartLMS

AI-powered multi-tenant LMS: Ionic/Angular app (web, Android, iOS via Capacitor), ASP.NET Core API, SQL Server, Python FastAPI AI service. All eight phases are built; read "Known limits" before relying on it. Nothing has been deployed.

## Quick start (Windows, local, no Docker)

1. Install: .NET 10 SDK, SQL Server (default local instance), Node.js 22+, Python 3.11+. Optional: Ollama.
2. Backend (PowerShell):
```powershell
cd backend\src\SmartLMS.API
dotnet dev-certs https --trust
dotnet user-secrets init
dotnet user-secrets set "Jwt:SigningKey" "<random string, 32+ characters>"
dotnet user-secrets set "AiService:ApiKey" "<random value, same as AI_SERVICE_KEY below>"
dotnet run --launch-profile https
```
Development settings seed a demo tenant and create the database on start (see `docs/PHASE2.md`). Open https://localhost:7200/swagger.
3. AI service (optional for everything except the AI features): see "Run the AI service" below.
4. Frontend:
```powershell
cd frontend
npm install
npm start
```
Open http://localhost:4200.

## Tests

```powershell
cd backend ; dotnet test SmartLMS.sln        # 139 tests, several minutes; or filter by class, e.g. --filter "FullyQualifiedName~LmsTests"
cd frontend ; npx ng test --watch=false      # needs Chrome installed
cd ai-service ; python -m pytest             # after pip install -r requirements-dev.txt
```
The backend tests run on SQLite, not SQL Server.

## Mobile (Capacitor)

```powershell
cd frontend
npm run build
npx cap add android      # once; needs Android Studio
npx cap sync
npx cap open android     # iOS: npx cap add ios on a Mac, then npx cap open ios
```
Set `apiBaseUrl` in `frontend/src/environments/environment.ts` to an address the phone can reach (an emulator reaches your PC at 10.0.2.2) and add that origin to `Cors:AllowedOrigins`. These commands were never run in the environment this was written in, and no native build was tested.

## Offline lessons and progress sync

In the native app a student can download a lesson (Downloads page or the player's Download button). Copies are stored in the app's private directory, expire after 14 days, are capped at 300 MB per lesson and are removed on logout. While offline, progress is queued on the device and replayed in order when the connection returns; an older replay never moves the saved position back. Web builds do not download. Details and limits: `docs/PHASE8.md`.

## Docker (optional)

See `docs/DEPLOYMENT.md`. `copy .env.example .env`, fill the secrets, `docker compose up --build`. Not run by the author (no Docker available); treat it as a starting point.

## Docs

| File | What |
|---|---|
| docs/ARCHITECTURE.md | Structure, decisions, trade-offs |
| docs/ERD.md and docs/erd/*.svg, *.png | ERD diagrams (Mermaid source in ERD.md, rendered SVG and PNG) per area |
| docs/DATA_DICTIONARY.md | Every table and column |
| docs/schema.sql | Reference SQL Server schema (105 tables, tenant-scoped composite FKs) |
| docs/API_CONTRACT.md and docs/api-contract.json | 220 endpoints, conventions, AI service contract |
| docs/SECURITY_LIMITS.md | What protections really do |
| docs/SCHEMA_AUDIT.md and docs/tenant_consistency_check.sql | SQL Server risk audit and a cross-tenant FK checker (schema.sql not executed on a live SQL Server) |
| docs/ARCHITECTURE_DIAGRAM.png | System diagram |
| docs/generator/ | Python scripts that generate the schema and API docs. Edit the `spec_*.py` files, then run `python gen_schema.py` and `python gen_api.py` |

## Prerequisites (Windows)

- .NET 10 SDK (`dotnet --version` should print 10.x)
- SQL Server (local default instance, Windows authentication). Needed from Phase 2.
- Node.js 22 LTS or newer, npm
- Python 3.11 or newer (service tests were run on 3.11.18 and 3.10; Windows not tested)
- Optional now, needed in Phase 5: Ollama with `llama3.1:8b` and `nomic-embed-text`

## Run the backend

```powershell
cd backend
dotnet dev-certs https --trust
dotnet build SmartLMS.sln
dotnet run --project src/SmartLMS.API --launch-profile https
```

Open https://localhost:7200/swagger and call `GET /api/v1/health`.

The connection string is in `backend/src/SmartLMS.API/appsettings.json` exactly as specified:
`Server=.;Database=SmartLMSDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true;`
The API opens it from Phase 2 on (see the Phase 2 section).

## Run the AI service

```powershell
cd ai-service
py -3.11 -m venv .venv
.venv\Scripts\activate
pip install -r requirements.lock.txt -r requirements-dev.txt
copy .env.example .env
```

Edit `.env` and set `AI_SERVICE_KEY` to a long random value (for example the output of `python -c "import secrets; print(secrets.token_urlsafe(32))"`). Put the same value in the .NET API with:

```powershell
cd ..\backend\src\SmartLMS.API
dotnet user-secrets init
dotnet user-secrets set "AiService:ApiKey" "<same value>"
```

Then:

```powershell
cd ..\..\..\..\ai-service
pytest
uvicorn app.main:app --host 127.0.0.1 --port 8001 --reload
```

Open http://localhost:8001/docs. `/health` works without a key; `/ai/*` returns 401 without `X-AI-Service-Key`, and works with it (Phase 5). Set `MEDIA_ROOT` in `.env` to the same folder as the API's `Storage:Root`, install Ollama and pull the two models (`ollama pull llama3.1:8b`, `ollama pull nomic-embed-text`) or choose a cloud provider. See `docs/PHASE5.md`.

## Phase 2 (database, tenancy, auth, devices)

See `docs/PHASE2.md` for design, run commands, tests and the list of limits. Short version:

```powershell
cd backend\src\SmartLMS.API
dotnet user-secrets init
dotnet user-secrets set "Jwt:SigningKey" "<random string, 32+ characters>"
dotnet run --launch-profile https
cd ..\..
dotnet test
```

## Phase 3

See `docs/PHASE3.md`. All 116 Phase 3 operations are built. SignalR hubs and Hangfire are set up; see the Slice E section for the query parameters and Hangfire settings. Same run commands as Phase 2, plus a required setting for certificate PDFs: `dotnet user-secrets set "Certificates:VerificationBaseUrl" "https://your-real-domain" --project backend/src/SmartLMS.API` (https only outside Development).

## Run the frontend

```powershell
cd frontend
npm install
npm start
```

Open http://localhost:4200 (Phase 6: login screen; backend must be running and the browser must trust the dev certificate).

Android/iOS: `npx cap add android` after `npm run build`, in Phase 6 and later. Not run in Phase 1.

## Regenerate docs

```powershell
cd docs\generator
python gen_schema.py
python gen_api.py
python gen_csharp.py
python test_tenant_isolation_sqlite.py
python reconcile_ef.py
```
(ERD SVGs are rendered separately with mermaid-cli from `docs/ERD.md`.)

## Secrets

None are committed. `AiService:ApiKey` and `AI_SERVICE_KEY` are empty by default. Use user-secrets, environment variables or `.env` (git-ignored).

## Frontend (Phase 6)
See `docs/PHASE6.md`. `cd frontend && npm install && npm start`, tests with `npm test`.

## Documentation map

PHASE2.md to PHASE8.md describe each phase. `docs/SECURITY_AUDIT.md` is the Phase 8 review, `docs/SECURITY_LIMITS.md` says what protections really do, `docs/DEPLOYMENT.md` the deployment checklist.

## Known limits

This is a development implementation, not a production or security certification (see `docs/SECURITY_AUDIT.md`).

- Tokens are stored in Capacitor Preferences, not the OS keystore. Offline lesson copies are not encrypted by the app (app-private folder, 14-day expiry).
- A lesson's first-ever heartbeat made offline credits at most 60 s of watched time (watched time decides subscription revenue, so this is deliberately conservative).
- No penetration test; the backend tests use SQLite, not SQL Server.
- Nothing was run end to end against a live .NET API, SQL Server and the AI service together. UI screenshots use a mock API.
- Offline downloads, Privacy Screen, biometric login and the voice tutor were not tested on a device.
- Docker files were not run. No penetration test was done. `pip-audit` still reports advisories in `chromadb` and `filelock` with no installable fix; why they do not apply to the embedded use here is in SECURITY_AUDIT.md.
- The `?ref=` referral link is unused; the admin affiliate form needs a pasted user ID; no SignalR client in the app; TA and SuperAdmin have no dashboard.

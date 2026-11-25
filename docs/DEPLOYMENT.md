# Deployment preparation

Nothing in this repository deploys anything. These files prepare a local or staging trial. **The compose stack and Dockerfiles were not run: Docker is not installed in the environment where they were written.** The compose YAML parses; the frontend `docker` build configuration was built with `ng build --configuration docker` and contains the same-origin `/api/v1` URL. The rest is untested.

## Files

| File | Purpose |
|---|---|
| `docker-compose.yml` | sqlserver, api, ai, web (nginx). Profile `llm` adds Ollama, profile `cache` adds Redis (the API does not use Redis yet, the profile is a placeholder service only). |
| `.env.example` | Placeholders only. Copy to `.env` (git-ignored). |
| `backend/Dockerfile`, `ai-service/Dockerfile`, `frontend/Dockerfile`, `frontend/nginx.conf` | Images. nginx serves the SPA and proxies `/api` and `/hubs`. |
| `.github/workflows/ci.yml` | Build and test only. No deploy step. |

## Trial run (after you install Docker)

```
copy .env.example .env      # then fill the three secrets
docker compose up --build
```
Open http://localhost:8080. The API applies EF migrations on start (`Database__AutoMigrate`) and seeds a demo tenant when `SEED_DEMO=true`. For the Ollama profile, run `docker compose --profile llm up` and then `docker compose exec ollama ollama pull llama3.1:8b` and `... nomic-embed-text`.

## Before a real deployment (checklist)

- [ ] Real secrets in a secret store, not `.env` on disk: `MSSQL_SA_PASSWORD` (prefer a dedicated SQL login, not `sa`), `JWT_SIGNING_KEY`, `AI_SERVICE_KEY`, gateway keys, SMTP password.
- [ ] TLS in front of nginx (certificate, redirect, HSTS). The compose file serves http.
- [ ] `SEED_DEMO=false`, `OTP_LOG_CODES=false`.
- [ ] `PUBLIC_ORIGIN` set to the https origin (certificate PDFs require https outside Development).
- [ ] Back up the `sqldata` and `storage` volumes; test a restore.
- [ ] Pin Python dependencies and resolve the `pip-audit` advisories (see SECURITY_AUDIT.md).
- [ ] Restrict SQL Server and the AI service ports; only `web` is published.
- [ ] Run the tests against the real SQL Server (the backend tests use SQLite).
- [ ] Build the mobile apps (`README.md`), sign them with your own keys, test on devices.
- [ ] Decide on a penetration test.

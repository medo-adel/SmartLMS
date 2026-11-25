# Security audit (Phase 8)

**Status: a development implementation, not a production or security certification.** Blockers before any production use: no penetration test or DAST; nothing run against real SQL Server, a real device or the real AI/gateway stack; Docker files never run; four unfixable `chromadb` advisories (not reachable in this app, see below); tokens in Capacitor Preferences instead of the OS keystore; offline copies not encrypted; a lesson's first-ever heartbeat made offline credits at most 60 s of watched time (conservative product decision); a TLS-terminating proxy, real secrets management and backups are the deployer's job (docs/DEPLOYMENT.md).

This is a code and configuration review plus automated checks. It is not a penetration test, and no DAST scanner, device test or live-environment test was run.

## What was checked and the result

| Check | Result |
|---|---|
| Reflective authorization test (`EndpointAuthorizationTests`): every mapped controller endpoint requires auth unless on the reviewed public list; every SignalR hub requires auth. Fails if a new action is added without `[Authorize]`, and if a public-list entry goes stale. | Passes (2 tests). |
| `npm audit --omit=dev` on the frontend | 0 vulnerabilities (run 2026-11-25, network to the npm registry). |
| `dotnet list package --vulnerable --include-transitive` on all 5 projects | No vulnerable packages against nuget.org. |
| `pip-audit` (2.10.1, PyPI JSON advisory source) on the AI service, Python 3.11.18 venv | Before: 22 rows = 21 unique advisories (17 filelock, 4 chromadb; PYSEC-2026-311 is listed twice). **filelock fixed** by upgrading to 4.3.1 (needs Python 3.11+, which the project already requires; the earlier sandbox venv was Python 3.10, which is why 4.2.1+ looked unavailable). **After: 4 unique chromadb advisories (5 rows) remain, no fixed version exists.** Raw output: `docs/pip-audit-2026-11-25.json` (after) . |
| Regex secrets scan over `backend/src`, `ai-service/app`, `frontend/src` | Nothing found. The scan is crude (pattern based), not a replacement for a tool such as gitleaks. |
| Config review | See below. |

## Public routes and why

Register, login, refresh, verify-email, OTP request/verify, forgot/reset password, social login (pre-auth by nature; login, OTP and voucher routes are rate limited). Payment gateway webhooks (called by the gateway, verified by signature, see PHASE4 docs). Public catalog reads (courses, bundles, plans, learning paths, categories), certificate verification by code, tenant branding and logo (needed before login), health. `integration/orders` and `integration/enrollments` have no JWT; they authenticate inside the action by `X-Api-Key` and scope.

## Findings fixed in Phase 8

1. **Security headers were missing.** Added `SecurityHeadersMiddleware`: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Permissions-Policy`, a CSP outside Development, `Cache-Control: no-store` for `/api` responses that set none, and HSTS outside Development. Test: `Responses_carry_security_headers_and_api_responses_are_not_cached`.
2. **Watched time could be inflated (revenue-qualifying) and offline replays could move the position back.** Heartbeats were only capped at 300 s each, so repeated or retried requests inflated `WatchedSeconds`, which qualifies lessons for subscription revenue. Now every heartbeat credits at most the elapsed wall-clock time, replays are idempotent by `RecordedAtUtc`, and future-dated records are not trusted (details and tradeoffs in `PHASE8.md`). Test: `Watched_time_never_exceeds_elapsed_time_whether_heartbeats_are_live_or_replayed_and_retries_are_ignored`.

## Python advisories: investigation and what remains

Sources: `pip-audit` JSON (`docs/pip-audit-2026-11-25.json`), the PyPI JSON API (`https://pypi.org/pypi/<pkg>/json`: filelock 4.3.1 latest, requires Python >=3.11, 0 listed vulnerabilities; chromadb 1.5.9 latest, 8 advisory records, none with a fixed version), and the GitHub advisory pages read on 2026-11-25.

**filelock: fixed.** All 17 advisories (CVE-2026-112906..112915, 112934, 112935, 112937..112941) were fixed in 4.2.1. `requirements.txt` now requires `filelock>=4.2.1` and `requirements.lock.txt` pins 4.3.1. 42 service tests pass on Python 3.11.18 with it. (Correction to an earlier draft that called this blocked: that was a Python 3.10 sandbox artefact.)

**chromadb 1.5.9: not fixable.** Affected range is `<= 1.5.9` for all four, patched versions "None" on GitHub, and 1.5.9 is the newest release.
- PYSEC-2026-311 / CVE-2026-45829 / GHSA-f4j7-r4q5-qw2c (>=1.0.0): unauthenticated code execution through `POST /api/v2/tenants/{tenant}/databases/{db}/collections` with `trust_remote_code`. This is a Chroma HTTP server endpoint. SmartLMS uses the embedded `chromadb.PersistentClient` (`ai-service/app/store.py`) and never starts the server, so the endpoint does not exist here. It also never creates collections from request data: two fixed collections are created at start-up with `embedding_function=None`.
- PYSEC-2026-3814 / CVE-2026-45833 / GHSA-36p7-vc44-83pf: same class, on the update-collection endpoint, requires UPDATE_COLLECTION permission on a Chroma server. Not reachable for the same reason.
- PYSEC-2026-3815 / CVE-2026-45831 / GHSA-xph7-9rjv-w5fr: `SimpleRBACAuthorizationProvider` ignores tenant/database/collection scope. SmartLMS configures no Chroma auth provider.
- PYSEC-2026-3813 / CVE-2026-45830 / GHSA-2wm9-hf6c-p5cr (HiddenLayer advisory): a caller holding a collection UUID can read or write any Chroma tenant's collection, because lookup by UUID skips the tenant filter in shared code (`SegmentAPI._get_collection`), so the embedded client has the same code. Precondition: the attacker must be able to run Chroma API calls with a chosen collection UUID. In SmartLMS only the AI service's own code calls Chroma, with its two fixed collections; no request field is ever used as a collection id. Tenant separation is done by the service itself: every query filters on `tenant_id` and `course_id`/`assignment_id` metadata (`store.py` has no unfiltered read). So the flaw is not reachable through the API.
- Residual risk: a future code change that passes client input to Chroma, or someone running a Chroma server next to this app. Mitigation: never run `chroma run`, keep the `chroma` volume private to the AI container, keep the AI service reachable only from the .NET API (shared secret), re-run `pip-audit` each release and upgrade as soon as a fixed chromadb exists.
- Not done: I did not read Chroma's source myself; applicability rests on the advisory texts and on this repository's own code.

## Configuration review (no change needed unless noted)

- CORS is an explicit origin list from config, no wildcard. Production must set `Cors:AllowedOrigins` (the compose file does).
- The JWT signing key has no default and comes from user-secrets or env. Access tokens are 15 minutes.
- Upload size limits exist for logos (2 MB), resources (25 MB) and media (200 MB) in `StorageOptions`. Kestrel's own request body limit was not changed from the default and was not tested.
- Behind a reverse proxy the API needs forwarded headers. The Docker image sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`. In that setup nginx talks plain http to the API, and TLS must be terminated in front of nginx; the compose file itself serves http only.
- `Otp:LogCodes` must stay false outside development (compose default is false).
- Hangfire dashboard is not mapped. In-memory Hangfire storage is refused in Production.

## Residual risks (not fixed)

- Offline lesson copies are not encrypted by the app. They live in the app-private data directory, expire after 14 days, and are removed on logout. A rooted or jailbroken device can read them.
- Offline access is not re-checked by the server until the device is online again (revoked enrollments keep working offline until expiry).
- Tokens are stored in Capacitor Preferences, not the OS keystore.
- Privacy Screen, watermark and blur are not a DRM; see `SECURITY_LIMITS.md`.
- Web-level only evidence for most UI behaviour; nothing was run on a physical Android or iOS device.
- No penetration test, DAST, the four unfixable chromadb advisories above, or secret scanning in CI.

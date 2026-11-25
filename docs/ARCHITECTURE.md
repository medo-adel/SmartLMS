# SmartLMS architecture (Phase 1)

## Shape

```
Ionic Angular app (web + Android/iOS via Capacitor)
        | HTTPS, JWT, X-Tenant-Id, X-Device-Id        SignalR (WebSocket)
        v
ASP.NET Core Web API (.NET 10 LTS)  ----------------->  SQL Server (SmartLMSDb, EF Core code-first)
  Clean Architecture                                     + Hangfire storage ([HangFire] schema)
        | HTTP + X-AI-Service-Key (server to server only)
        v
Python FastAPI AI service (Pydantic v2)  -->  ChromaDB (local files)  /  Ollama or OpenAI or Gemini  /  Faster-Whisper
```

The Ionic app never talks to the AI service. The .NET API checks authentication, tenant, device, enrollment and rate limits, then forwards.

## Decisions and why

| Decision | Choice | Reason |
|---|---|---|
| .NET version | **.NET 10 LTS** (the brief allowed 8 or 10) | Microsoft's release metadata (builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json, read 2026-11-24) lists .NET 8 as end-of-life on 2026-11-10 and .NET 10 as LTS until 2028-11-14. Packages are pinned to 10.0.x. .NET 11 is listed as STS, so it is not used. |
| Application layer | **Service layer** + FluentValidation + Mapster | The brief allowed MediatR or a service layer. Service layer keeps the code plain and adds no licensing question. (For reference: MediatR 13+ asks for a license key per the vendor's FAQ at luckypennysoftware.com/faq; check their terms if you want CQRS later.) |
| Tenancy | Shared database. Every tenant-owned table has `TenantId` and references other tenant-owned rows through **composite foreign keys `(TenantId, Id)`**; references to users go through `UserTenantMemberships (TenantId, UserId)`. EF global query filters are a second layer. | The database itself rejects a row that points at another tenant's data, even if application code has a bug. Cost: 188 composite FKs, one extra alternate key per referenced table, and every EF relationship in Phase 2 must be configured with the composite key. |
| Identity users | Guid keys, global `AspNetUsers` (nullable `TenantId`, NULL only for SuperAdmin), `UserName = "<tenantSlug>:<email>"` | Lets the same email register in two tenants. Tenant data never references `AspNetUsers` directly; it references the user's membership in that tenant. A SuperAdmin works inside a tenant only through an explicit, audited `SupportAccess` membership row (with expiry). Platform-level structures (Tenants, roles, AuditLogs with NULL tenant, RefreshTokens, OtpCodes, UserDevices) are intentionally not tenant-scoped by composite keys; see SCHEMA_AUDIT.md. |
| IDs | `uniqueidentifier` with `NEWSEQUENTIALID()` default | Index friendly. EF will generate Guids client-side. |
| Delete model | Soft delete on most business tables, filtered unique indexes | Recycle bin requirement. FKs use NO ACTION. |
| Money | `decimal(18,2)`, append-only `WalletTransactions`, RowVersion on `Wallets`, idempotency keys | Prevents double credit and lost updates. |
| Vouchers | Store SHA-256 of the code only | A database leak does not expose unused codes. Trade-off: the plain CSV exists once at generation. |
| Files | Metadata in `FileAssets`, bytes on disk or object storage | Keeps SQL small. Storage backend is chosen in Phase 3. |
| Frontend | Angular 22 standalone + Ionic 9 | Brief asks for Ionic 8+ and Angular 18+; these are the current releases. The app is zoneless (Angular default). |
| AI service | FastAPI + Pydantic v2, Python 3.11+ | As requested. Previous Flask project is untouched. |
| Vector partitions | One Chroma collection per tenant for course material, metadata `course_id` / `lesson_id`; separate collection for assignment embeddings | Cheap tenant isolation at the vector store. |

## Solution layout

```
SmartLMS/
  backend/                  .NET solution (SmartLMS.sln)
    src/SmartLMS.Domain         entities, enums, value objects, domain exceptions, IAuditableEntity, ISoftDeletable, ITenantEntity
    src/SmartLMS.Application    DTOs, services/interfaces, validators, ApiResponse<T>, PagedResult<T>
    src/SmartLMS.Infrastructure EF Core, Identity/JWT, Hangfire, file storage, email/FCM, AI HTTP client, webhook dispatcher
    src/SmartLMS.API            controllers, middleware, SignalR hubs, Swagger
  ai-service/               FastAPI app (app/), tests/, .env.example
  frontend/                 Ionic Angular standalone app
  docs/                     this folder (schema, ERD, API contract)
```

Dependency rule: Domain depends on nothing. Application depends on Domain. Infrastructure depends on Application. API depends on Application and Infrastructure.

## Cross-cutting design

**Request pipeline (Phase 2 target):** exception middleware, HTTPS, CORS, authentication, tenant resolution (header or subdomain, must match token claim), device binding check (active `UserDevices` row for `X-Device-Id`), authorization, rate limiting, controller.

**Background work:** Hangfire jobs for voucher generation, ingest/transcription, plagiarism batches, certificate PDFs, webhook delivery, analytics rollups. Each job has a `BackgroundJobs` row (`Pending`, `Processing`, `Completed`, `Failed`). Status changes are pushed over SignalR.

**Audit:** an EF `SaveChanges` interceptor writes `AuditLogs` for sensitive entities (grades, courses, payments, vouchers, devices, role changes) and fills the auditable and soft-delete columns.

**Real time:** SignalR hubs for notifications and job status, chat, and live class presence.

**Localization:** UI strings in `en` and `ar`; `dir` switches between `ltr` and `rtl` at runtime. Server messages follow `Accept-Language`.

## What Phase 1 contains

Working: solution that builds, `GET /api/v1/health`, Swagger, `ApiResponse<T>`, paging types, global exception middleware, marker interfaces, role names, config; FastAPI app with service-key auth and 11 typed routes (return 501); Ionic app shell that builds and calls the health endpoint; full schema, ERD, data dictionary and API contract.

Not built yet: entities and migrations, Identity, JWT, device binding, every business module, Hangfire and SignalR wiring, AI logic, real UI. Those belong to Phases 2 to 8.

## Phase 2 plan changes from tenant-scoped keys

- EF entities carry `TenantId`; every relationship to a tenant-owned entity uses `HasForeignKey(TenantId, X).HasPrincipalKey(TenantId, Id)` (alternate key). User references use the `UserTenantMemberships` entity.
- Registering a user creates the Home membership in the same transaction. SuperAdmin support access creates a `SupportAccess` row with `ExpiresAtUtc` and an `AuditLogs` entry.
- Invariants the database cannot enforce (see SCHEMA_AUDIT.md): a Home membership's `TenantId` equals `AspNetUsers.TenantId`; `SupportAccess` is only for SuperAdmin. Covered by service code, tests and `tenant_consistency_check.sql`.
- Phase 2 tests: cross-tenant insert attempts must fail at the database (run against a real SQL Server LocalDB/Express); query filter tests; SuperAdmin without membership gets nothing.
- Composite FKs require the `UserDevices (UserId, Id)` alternate key for device references.

## Reconciling schema and migrations

`docs/schema.sql` is generated from a spec to review the design. In Phase 2 the EF model becomes the source of truth and the first migration must be diffed against this design. Differences should be written down, not left silent.

# Phase 2: database, tenancy, auth and device control

Scope: domain model, EF Core, tenant isolation, Identity + JWT, device binding, validation, Swagger, seeder, initial migration, tests.
Not in scope (Phase 3 stays gated): courses, enrollments, content, exams, payments, AI, mobile.

## What exists

| Area | Where |
|---|---|
| 100 entities + string enums + EF mapping (generated from `docs/generator/spec_schema.py`) | `Domain/Entities`, `Domain/Enums/Generated`, `Infrastructure/Persistence/Generated` |
| `ApplicationDbContext` (Identity, query filters, soft delete) | `Infrastructure/Persistence/ApplicationDbContext.cs` |
| Audit interceptor (audit columns, soft delete, tenant write guard, `AuditLogs`) | `Infrastructure/Persistence/AuditSaveChangesInterceptor.cs` |
| Tenant resolution + SuperAdmin support access | `API/Middleware/TenantResolutionMiddleware.cs` |
| Device binding | `API/Middleware/DeviceBindingMiddleware.cs` |
| Global exception middleware (stable error codes) | `API/Middleware/GlobalExceptionMiddleware.cs` |
| Auth, OTP, refresh tokens, users, devices, tenants | `Infrastructure/Services/*` and `API/Controllers/*` |
| FluentValidation | `Application/Validators.cs`, `API/Filters/ValidationFilter.cs` |
| Swagger with Bearer + `X-Tenant-Id` / `X-Device-Id` | `API/Program.cs`, `API/Swagger` |
| Seeder | `Infrastructure/Services/DatabaseSeeder.cs` |
| Migration `InitialCreate` + generated script | `Infrastructure/Persistence/Migrations`, `docs/ef-migration.sql` |
| Tests (38) | `backend/tests/SmartLMS.Tests` |

The Phase 2 rows of `API_CONTRACT.md` are live (including support-access and logo upload/serve). One is a deferred hook, not counted as implemented: `POST /auth/social/{provider}` returns `501 NOT_IMPLEMENTED`.

## Tenant isolation, in layers

1. **Database**: 188 composite `(TenantId, X)` foreign keys. EF mapping is generated from the same spec, and `python docs/generator/reconcile_ef.py` proves the EF migration has the same 107 tables, the same columns/nullability and the same 302 foreign key shapes as `schema.sql`.
2. **Query filters**: every tenant-owned entity is filtered by `CurrentTenantId`. With no tenant resolved, queries return nothing (fails closed; a test found and fixed a null-equals-null leak while this was written). Platform scope (SuperAdmin on `/tenants`, `/me`, `/auth`) only sees platform rows and the `Tenants` table; it does not widen tenant-owned data.
3. **Interceptor**: a write whose `TenantId` is not the current tenant throws `TENANT_MISMATCH`.
4. **Middleware**: the token's `tid` must equal `X-Tenant-Id` (else 403 `TENANT_MISMATCH`). A SuperAdmin token has no `tid`; tenant routes need an unexpired, unrevoked `SupportAccess` membership (403 `SUPPORT_ACCESS_REQUIRED`), granted with `POST /tenants/{id}/support-access` (default 60 min, max 480, audited).

Identity: `UserName` is `<tenantSlug>:<email>` (`platform:<email>` for SuperAdmin). Login resolves the tenant first, so one email can be a user in several tenants with separate accounts. Platform login: call `/auth/login` without `X-Tenant-Id`.

## Auth and devices

- Access token 15 min (claims `sub`, `tid`, `role`, `did`, `email`, `name`). Refresh token: 64 random bytes, only its SHA-256 is stored, rotated on every use. Reusing a rotated token revokes that device's live tokens.
- Password: 10+ chars, upper, lower, digit. 5 failures lock the account for 15 minutes. `auth` endpoints are rate limited to 10 requests/minute/IP.
- OTP: 6 digits, hash stored, 10 minutes, 5 attempts. Forgot-password and OTP request answer the same whether or not the account exists.
- Device binding: every authenticated call needs `X-Device-Id` (the fingerprint used at login). It is hashed and compared with the active `UserDevices` row from the token's `did` claim, per request, against the database. A device reset or deactivation takes effect immediately.
- Device cap applies to **Student** accounts only, per type (Mobile/Web), from `Tenants.MaxMobileDevices` / `MaxWebDevices`. Staff can sign in on several devices.
- No secrets in the repo. `Jwt:SigningKey` (32+ bytes) is required and the app refuses to start without it.

## Run it (Windows PowerShell, SQL Server on `.`)

```powershell
cd backend\src\SmartLMS.API
dotnet user-secrets init
dotnet user-secrets set "Jwt:SigningKey" "<random string, 32+ characters>"
dotnet user-secrets set "Seed:SuperAdminEmail" "you@example.com"
dotnet user-secrets set "Seed:SuperAdminPassword" "<your own strong password>"
dotnet run --launch-profile https
```
Development settings apply the migration and seed on start (`Database:AutoMigrate`, `Seed:Enabled`, roles, demo tenant `demo`, and the SuperAdmin only if you set the two secrets). To apply the migration yourself:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.*
cd backend
dotnet ef database update --project src\SmartLMS.Infrastructure --startup-project src\SmartLMS.API
```
Or run `docs\ef-migration.sql` in SSMS. Swagger: https://localhost:7200/swagger. Tests: `cd backend; dotnet test`.

## Security corrections (second pass)

- **Device cap race fixed.** Binding a device takes a write lock on the user's own row inside a transaction, then counts, then inserts, so simultaneous logins for one user run one at a time. Tests: 8 and 10 parallel logins with caps 1 and 2 end with exactly the cap. Removing the lock makes those tests fail (checked).
- **Refresh rotation is atomic.** The old token is claimed with a conditional UPDATE (`RevokedAtUtc IS NULL`), so exactly one of N parallel refreshes wins; the rest count as reuse and revoke the device's live tokens. Test: 8 parallel refreshes, exactly 1 success. A genuine double-tap by a client therefore ends that device's session; this is deliberate.
- **Revocation tests**: logout revokes the token; logout cannot revoke another user's token; device reset kills the old access token (next call 403) and refresh token; changing the password revokes other devices' refresh tokens and keeps the current one.
- **Instructor scope.** Instructors see and manage only students holding a non-revoked enrollment in a course the instructor owns (`InstructorScope.StudentsOfInstructor`). With no enrollment (all of Phase 2) they see nobody; unrelated or non-student accounts answer 404, not 403, so existence is not disclosed. Negative tests cover: no enrollment, unrelated student, other instructor, staff accounts, creating users, changing roles, deactivating, device list and reset. Teaching assistants have no user access at all in Phase 2.
- **Email.** OTPs go through `IEmailTransport`. Default is a do-nothing transport. SMTP is used only if `Email:Enabled=true` and `Email:Host` and `Email:FromAddress` are set (password via user-secrets `Email:Password`). Tests: transport selection (4 combinations), message content, phone destinations skipped (no SMS provider exists), and the SMTP transport against a local loopback fake server. It has not been run against a real mail server, and no real email is sent by anything in this project.
- **Logo upload implemented**: TenantAdmin only, multipart field `file`, PNG/JPEG/WebP by file bytes, max 2 MB, stored under `Storage:Root/tenants/<tenantId>/branding/`, recorded in `FileAssets`, served publicly (like branding) at `GET /api/v1/tenants/current/logo` with `nosniff`. Tests cover: wrong role, no auth, token/header tenant mismatch, SVG with script, text with a .png name, oversize, tenant B cannot see tenant A's logo, rejected upload keeps the old logo. Local disk only; object storage comes later.
- **Migration delete behavior.** Identity's default `ON DELETE CASCADE` (user/role claims, logins, tokens, user-roles) was removed; every foreign key is `NO ACTION`, as in `schema.sql`. Cascade graph: before the change the only cascades were from `AspNetUsers`/`AspNetRoles` to five leaf tables that nothing references, so SQL Server's "multiple cascade paths" error was not expected; it is moot now. This was reasoned from the generated script, not run on SQL Server.

## Known differences and limits (read these)

- **Not run against SQL Server or Windows.** Built and tested on Linux with .NET 10.0.404. Tests use SQLite (file database, WAL), which approximates but does not equal SQL Server. The row lock, the conditional UPDATE and the composite foreign keys are standard SQL Server features but their behavior there is untested. JSON `ISJSON` checks and `rowversion` are skipped on SQLite.
- **The EF migration is structurally compared with `schema.sql`, not identical.** `reconcile_ef.py` checks tables, columns, nullability, foreign key shapes and referential actions (0 differences). It does not compare index definitions, check constraints or defaults. Known differences: EF has no column DEFAULT constraints (defaults live in C# initializers); ids are client-generated GUID v7; index and check-constraint names follow EF conventions; `AspNetUsers` has an extra non-unique `UserNameIndex`. Use one of the two to create a database, not both.
- Tenant lookup is cached for 30 s, so deactivating a tenant can take up to 30 s to apply.
- Phone OTP requests (`purpose=PhoneVerification`) return 501 `SMS_NOT_CONFIGURED` and issue no code, so phone verification cannot complete and nothing implies a code was sent (test: `Phone_otp_request_fails_visibly_and_issues_no_code`). Email OTPs are unchanged.
- (old note) Phone OTPs are not delivered (no SMS provider). Email delivery is built but off until you configure it. In Development the code is also logged (`Otp:LogCodes`).
- Social login is a deferred hook (501). Email verification is OTP only; there is no link-token flow.
- `X-Tenant-Id` is optional for tenant users (their token carries the tenant) and mandatory for anonymous calls and SuperAdmin support access. Refresh by a tenant user without the header fails.
- Biometric enable is stored per user, not per device.
- Hangfire, SignalR and the AI client are not wired in yet (later phases).


## OpenAPI versus contract (checked by method + path)

The running API's OpenAPI document has 37 operations (31 paths). The contract (`docs/api-contract.json`) labels 1 operation Phase 1 (`GET /health`) and 36 Phase 2. Compared on method + path with route parameters normalised: 0 contract Phase 1/2 operations missing from OpenAPI, 0 extra operations in OpenAPI, and 0 Phase 3 to 5 operations exposed. The other 186 contract operations are Phase 3 to 5 and are not built. Social login (`POST /auth/social/{provider}`) is in the 37 as a deferred 501 hook.

# Schema audit (SQL Server rules a syntax parser cannot check)

**Limit: no live SQL Server was available. `schema.sql` has NOT been executed.** The checks below were run by `docs/generator/gen_schema.py` against the spec, and `schema.sql` was parsed with sqlglot's T-SQL dialect (syntax only). Run the script on a dev SQL Server (or let EF migrations create the schema in Phase 2) before trusting it.

| Risk | Result | How checked |
|---|---|---|
| Multiple cascade paths (error 1785) | None possible. No FK uses ON DELETE/UPDATE CASCADE or SET NULL. Deletes are soft, or blocked by NO ACTION. | Scan of generated SQL. |
| Cycles / FK creation order | FKs are added with ALTER TABLE after all tables exist. Self references (Categories, DiscussionPosts, Lessons) are fine with NO ACTION. | By construction. |
| Constraint and index name collisions, 128 char limit | None | Script check. |
| Index key size | All keys under 1700 bytes (nonclustered limit); PKs are GUIDs | Script estimate from declared column sizes. |
| Filtered unique indexes with soft delete | Unique indexes on soft-deletable tables use `WHERE IsDeleted = 0`. Consequence: a soft-deleted row no longer blocks reuse of its slug or code, and restoring it can fail if the value was reused. The restore endpoint must handle that conflict. | Design review. |
| Nullable columns in unique indexes | Single nullable columns (Tenants.Subdomain) are filtered `IS NOT NULL`. Submissions and DiscussionVotes use two filtered indexes because SQL Server treats NULLs as equal in unique indexes. | Design review. |
| Composite scoped uniqueness | Uniqueness is per tenant where the business value is tenant-scoped (Courses slug, Coupons code, Categories slug, Badges code, Invoice number, Voucher hash, AffiliateProfiles code). Pairs of GUIDs (UserId, CourseId) are tenant-safe because GUIDs are unique across tenants. AspNetUsers is unique on (TenantId, NormalizedUserName); Identity's own default global unique index on NormalizedUserName must be replaced in the Phase 2 Identity configuration. | Design review. |
| **Tenant-crossing FKs** | **Enforced by the database design.** 188 foreign keys between tenant-owned tables are composite `(TenantId, X) -> (TenantId, Id)`, backed by `UNIQUE (TenantId, Id)` alternate keys, so a row of tenant A cannot reference a row of tenant B. User references use `UserTenantMemberships (TenantId, UserId)`, so a user must be a member of the tenant. Device references use `UserDevices (UserId, Id)`. **Intentionally not scoped:** `Tenants`, `AspNetUsers`/roles (global identity, NULL tenant for SuperAdmin), `UserTenantMemberships.UserId`/`GrantedBy` -> `AspNetUsers`, and the nullable-tenant tables `RefreshTokens`, `OtpCodes`, `UserDevices`, `AuditLogs` (8 plain FKs, listed in `tenant_consistency_check.sql`). Rules the database cannot express (Home membership TenantId equals the user's TenantId; SupportAccess only for SuperAdmin) are enforced in code and verified by that script. | `test_tenant_isolation_sqlite.py` builds the whole generated FK graph in SQLite and proves cross-tenant inserts fail (it also proves every composite FK target has a matching unique key). **This tests the key design in SQLite, not SQL Server.** |
| Check constraints | Enum CHECKs, JSON validity (`ISJSON`), exactly-one-target rules (Submissions, DiscussionVotes, Certificates, CartItems, OrderItems, ExamQuestionRules), non-negative wallet balance. | Parsed only. |
| Intentionally no FK | CreatedBy, UpdatedBy, DeletedBy (avoid FK noise and ordering), DiscussionThreads.AcceptedPostId (circular with posts). | Design choice. |
| Hangfire | Creates its own `[HangFire]` schema; not in `schema.sql`. | n/a |

Composite FK nullability: SQL Server (like SQLite) does not check a composite FK when any of its columns is NULL (MATCH SIMPLE). Tenant-owned tables have a NOT NULL `TenantId`, so optional references like `ReviewedBy` with a NULL user are skipped as intended and non-NULL ones are checked on both columns.

EF Core will generate its own names and may differ slightly (for example Identity key types and index names). Reconcile in Phase 2.

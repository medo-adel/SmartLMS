# Data dictionary

Generated from `docs/generator/spec_schema.py`. Columns shown include the standard ones each table gets from its flags.

Flags: **T** tenant-owned (TenantId, global query filter), **N** nullable TenantId, **A** auditable, **S** soft-deletable.

## Tenancy

### Tenants

Flags: `AS`. One row per white-label customer. Not itself tenant-filtered.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| Name | nvarchar(200) | no |  |
| Slug | nvarchar(100) | no | unique |
| Subdomain | nvarchar(100) | yes | unique |
| LogoUrl | nvarchar(500) | yes |  |
| PrimaryColor | nvarchar(9) | yes |  |
| SecondaryColor | nvarchar(9) | yes |  |
| AccentColor | nvarchar(9) | yes |  |
| DefaultLanguage | nvarchar(10) | no | default 'en' |
| TimeZoneId | nvarchar(64) | no | default 'Africa/Cairo' |
| Currency | nvarchar(3) | no | default 'EGP' |
| MaxMobileDevices | int | no | default 1 |
| MaxWebDevices | int | no | default 1 |
| IsActive | bit | no | default 1 |
| InstructorSharePercent | float | yes |  |
| SubscriptionMinWatchPercent | float | yes |  |
| SubscriptionMinAvgLessonSeconds | int | yes |  |
| SubscriptionJumpAlertPercent | float | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

### TenantFeatureFlags

Flags: `TA`. Tenant-level feature toggles such as wallet, affiliates, live_classes, ai_tutor.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| FeatureKey | nvarchar(100) | no |  |
| IsEnabled | bit | no | default 0 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (TenantId, FeatureKey).

## Identity & Auth

### AspNetUsers

Flags: `NAS`. ASP.NET Core Identity user (Guid key). TenantId is NULL only for SuperAdmin. UserName is stored as '<tenantSlug>:<email>' so the same email can exist in two tenants.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | yes | FK (TenantId) -> Tenants(Id) |
| FullName | nvarchar(200) | no |  |
| UserName | nvarchar(256) | no |  |
| NormalizedUserName | nvarchar(256) | no |  |
| Email | nvarchar(256) | yes |  |
| NormalizedEmail | nvarchar(256) | yes |  |
| EmailConfirmed | bit | no | default 0 |
| PasswordHash | nvarchar(max) | yes |  |
| SecurityStamp | nvarchar(max) | yes |  |
| ConcurrencyStamp | nvarchar(max) | yes |  |
| PhoneNumber | nvarchar(32) | yes |  |
| PhoneNumberConfirmed | bit | no | default 0 |
| TwoFactorEnabled | bit | no | default 0 |
| LockoutEnd | datetimeoffset(7) | yes |  |
| LockoutEnabled | bit | no | default 1 |
| AccessFailedCount | int | no | default 0 |
| AvatarUrl | nvarchar(500) | yes |  |
| PreferredLanguage | nvarchar(10) | no | default 'en' |
| IsActive | bit | no | default 1 |
| LastLoginAtUtc | datetime2(7) | yes |  |
| BiometricLoginEnabled | bit | no | default 0 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: unique (TenantId, NormalizedUserName).

### AspNetRoles

Flags: `-`. Global roles: SuperAdmin, TenantAdmin, Instructor, TeachingAssistant, Student, Parent.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| Name | nvarchar(256) | no |  |
| NormalizedName | nvarchar(256) | no | unique |
| ConcurrencyStamp | nvarchar(max) | yes |  |

### AspNetUserRoles

Flags: `-`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| UserId | uniqueidentifier | no | FK (UserId) -> AspNetUsers(Id) |
| RoleId | uniqueidentifier | no | FK (RoleId) -> AspNetRoles(Id) |

Indexes: unique (UserId, RoleId).

### AspNetRoleClaims

Flags: `-`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | int | no | PK |
| RoleId | uniqueidentifier | no | FK (RoleId) -> AspNetRoles(Id) |
| ClaimType | nvarchar(max) | yes |  |
| ClaimValue | nvarchar(max) | yes |  |

### AspNetUserTokens

Flags: `-`. ASP.NET Core Identity token store (authenticator keys, recovery codes). Composite primary key.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| UserId | uniqueidentifier | no | FK (UserId) -> AspNetUsers(Id) |
| LoginProvider | nvarchar(128) | no |  |
| Name | nvarchar(128) | no |  |
| Value | nvarchar(max) | yes |  |

### AspNetUserClaims

Flags: `-`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | int | no | PK |
| UserId | uniqueidentifier | no | FK (UserId) -> AspNetUsers(Id) |
| ClaimType | nvarchar(max) | yes |  |
| ClaimValue | nvarchar(max) | yes |  |

### AspNetUserLogins

Flags: `-`. Social login hooks (Google, Facebook, Apple).

| Column | Type | Null | Key / rule |
|---|---|---|---|
| LoginProvider | nvarchar(128) | no |  |
| ProviderKey | nvarchar(128) | no |  |
| ProviderDisplayName | nvarchar(max) | yes |  |
| UserId | uniqueidentifier | no | FK (UserId) -> AspNetUsers(Id) |

Indexes: unique (LoginProvider, ProviderKey).

### UserTenantMemberships

Flags: `TA`. Tenant membership of a global identity. EVERY user-to-tenant-data reference points here as (TenantId, UserId), so a row can only reference a user who is a member of the same tenant. Each tenant user has one Home row; a SuperAdmin acts inside a tenant only through an explicit, audited SupportAccess row.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (UserId) -> AspNetUsers(Id) |
| Kind | nvarchar(40) | no | one of: Home, SupportAccess; default Home |
| GrantedBy | uniqueidentifier | yes | FK (GrantedBy) -> AspNetUsers(Id) |
| ExpiresAtUtc | datetime2(7) | yes |  |
| RevokedAtUtc | datetime2(7) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### RefreshTokens

Flags: `NA`. Rotating refresh tokens. Only the SHA-256 hash is stored. Reuse of a revoked token revokes the whole chain.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | yes | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (UserId) -> AspNetUsers(Id) |
| DeviceId | uniqueidentifier | yes | FK (DeviceId) -> UserDevices(Id) |
| TokenHash | nvarchar(128) | no | unique |
| ExpiresAtUtc | datetime2(7) | no |  |
| RevokedAtUtc | datetime2(7) | yes |  |
| ReplacedByTokenHash | nvarchar(128) | yes |  |
| CreatedByIp | nvarchar(64) | yes |  |
| RevokedReason | nvarchar(200) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### OtpCodes

Flags: `NA`. Short-lived OTPs. Hash only; max attempts enforced in code.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | yes | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | yes | FK (UserId) -> AspNetUsers(Id) |
| Destination | nvarchar(256) | no |  |
| Purpose | nvarchar(40) | no | one of: EmailVerification, PhoneVerification, PasswordReset, Login |
| CodeHash | nvarchar(128) | no |  |
| ExpiresAtUtc | datetime2(7) | no |  |
| ConsumedAtUtc | datetime2(7) | yes |  |
| AttemptCount | int | no | default 0 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: index (Destination, Purpose).

## Anti-Piracy

### UserDevices

Flags: `NA`. Device binding. Active devices per user and DeviceType are capped by Tenants.MaxMobileDevices / MaxWebDevices.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | yes | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (UserId) -> AspNetUsers(Id) |
| DeviceFingerprintHash | nvarchar(128) | no |  |
| DeviceType | nvarchar(40) | no | one of: Mobile, Web |
| Platform | nvarchar(30) | yes |  |
| DeviceName | nvarchar(200) | yes |  |
| UserAgent | nvarchar(500) | yes |  |
| FirstSeenAtUtc | datetime2(7) | no |  |
| LastSeenAtUtc | datetime2(7) | no |  |
| LastIp | nvarchar(64) | yes |  |
| IsActive | bit | no | default 1 |
| RevokedAtUtc | datetime2(7) | yes |  |
| RevokedBy | uniqueidentifier | yes | FK (RevokedBy) -> AspNetUsers(Id) |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (UserId, DeviceFingerprintHash).

### DeviceResetLogs

Flags: `TA`. Who reset which student's devices and when (also written to AuditLogs).

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| ResetByUserId | uniqueidentifier | no | FK (TenantId, ResetByUserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| DeviceId | uniqueidentifier | yes | FK (UserId, DeviceId) -> UserDevices(UserId, Id) tenant-scoped |
| Reason | nvarchar(500) | yes |  |
| ResetAtUtc | datetime2(7) | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### PlaybackSessions

Flags: `T`. Heartbeat log used to detect concurrent playback of one account on many devices. Detection signal, not a block by itself.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| LessonId | uniqueidentifier | no | FK (TenantId, LessonId) -> Lessons(TenantId, Id) tenant-scoped |
| DeviceId | uniqueidentifier | yes | FK (UserId, DeviceId) -> UserDevices(UserId, Id) tenant-scoped |
| StartedAtUtc | datetime2(7) | no |  |
| LastHeartbeatAtUtc | datetime2(7) | no |  |
| EndedAtUtc | datetime2(7) | yes |  |
| Ip | nvarchar(64) | yes |  |

Indexes: index (UserId, LessonId).

## Files & Jobs

### FileAssets

Flags: `TAS`. Metadata for every stored file. Bytes live on disk or object storage, never in SQL.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| OriginalName | nvarchar(260) | no |  |
| StoragePath | nvarchar(500) | no |  |
| ContentType | nvarchar(100) | no |  |
| SizeBytes | bigint | no |  |
| Sha256 | nvarchar(64) | yes |  |
| MeasuredDurationSeconds | int | yes |  |
| UploadedBy | uniqueidentifier | yes | FK (TenantId, UploadedBy) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| Purpose | nvarchar(40) | no | one of: CourseMedia, LessonResource, Receipt, Submission, Certificate, Invoice, Avatar, Export, Other |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: index (Sha256).

### BackgroundJobs

Flags: `TA`. Application-level job status mirrored to clients over SignalR. Hangfire keeps its own [HangFire] schema.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| Type | nvarchar(40) | no | one of: AiIngest, AiTranscribe, PlagiarismBatch, VoucherGeneration, WebhookDelivery, CertificatePdf, AnalyticsRollup, Email, Export, PeerReviewAssign, PayoutCalculation |
| Status | nvarchar(40) | no | one of: Pending, Processing, Completed, Failed |
| HangfireJobId | nvarchar(64) | yes |  |
| ReferenceType | nvarchar(60) | yes |  |
| ReferenceId | uniqueidentifier | yes |  |
| RequestedBy | uniqueidentifier | yes | FK (TenantId, RequestedBy) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| ProgressPercent | int | no | default 0 |
| ErrorMessage | nvarchar(2000) | yes |  |
| ResultJson | nvarchar(max) | yes |  |
| StartedAtUtc | datetime2(7) | yes |  |
| CompletedAtUtc | datetime2(7) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: index (Status, Type).

## Course Catalog

### Categories

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| ParentCategoryId | uniqueidentifier | yes | FK (TenantId, ParentCategoryId) -> Categories(TenantId, Id) tenant-scoped |
| Name | nvarchar(200) | no |  |
| Slug | nvarchar(200) | no |  |
| SortOrder | int | no | default 0 |
| IconName | nvarchar(60) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: unique (TenantId, Slug).

### Courses

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| CategoryId | uniqueidentifier | yes | FK (TenantId, CategoryId) -> Categories(TenantId, Id) tenant-scoped |
| OwnerInstructorId | uniqueidentifier | no | FK (TenantId, OwnerInstructorId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| Title | nvarchar(250) | no |  |
| Slug | nvarchar(250) | no |  |
| ShortDescription | nvarchar(500) | yes |  |
| Description | nvarchar(max) | yes |  |
| ThumbnailFileId | uniqueidentifier | yes | FK (TenantId, ThumbnailFileId) -> FileAssets(TenantId, Id) tenant-scoped |
| PromoVideoUrl | nvarchar(500) | yes |  |
| Level | nvarchar(40) | no | one of: Beginner, Intermediate, Advanced, AllLevels |
| Language | nvarchar(10) | no | default 'en' |
| Status | nvarchar(40) | no | one of: Draft, PendingApproval, Published, Rejected, Archived; default Draft |
| PricingModel | nvarchar(40) | no | one of: Free, OneTime, PayPerLesson, SubscriptionOnly; default Free |
| Price | decimal(18,2) | no | default 0 |
| Currency | nvarchar(3) | no | default 'EGP' |
| DripMode | nvarchar(40) | no | one of: None, ByDate, DaysAfterEnrollment; default None |
| AccessDays | int | yes |  |
| EstimatedHours | float | yes |  |
| PublishedAtUtc | datetime2(7) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: unique (TenantId, Slug); index (TenantId, Status).

### CourseStaff

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| StaffRole | nvarchar(40) | no | one of: CoInstructor, TeachingAssistant |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (CourseId, UserId).

### CourseApprovals

Flags: `TA`. Instructor course approval workflow history.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| SubmittedBy | uniqueidentifier | no | FK (TenantId, SubmittedBy) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| SubmittedAtUtc | datetime2(7) | no |  |
| ReviewedBy | uniqueidentifier | yes | FK (TenantId, ReviewedBy) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| ReviewedAtUtc | datetime2(7) | yes |  |
| Decision | nvarchar(40) | no | one of: Pending, Approved, Rejected; default Pending |
| Comment | nvarchar(1000) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### CoursePrerequisites

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| PrerequisiteCourseId | uniqueidentifier | no | FK (TenantId, PrerequisiteCourseId) -> Courses(TenantId, Id) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (CourseId, PrerequisiteCourseId).

### Sections

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| Title | nvarchar(250) | no |  |
| Description | nvarchar(1000) | yes |  |
| SortOrder | int | no | default 0 |
| DripUnlockAtUtc | datetime2(7) | yes |  |
| DripDaysAfterEnrollment | int | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: index (CourseId, SortOrder).

### Lessons

Flags: `TAS`. Video files are stored as FileAssets; streaming and DRM strategy is a Phase 3 decision (see docs/SECURITY_LIMITS.md).

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| SectionId | uniqueidentifier | no | FK (TenantId, SectionId) -> Sections(TenantId, Id) tenant-scoped |
| Title | nvarchar(250) | no |  |
| LessonType | nvarchar(40) | no | one of: Video, Article, Pdf, Audio, ExternalLink, InteractiveSlides |
| ContentMarkdown | nvarchar(max) | yes |  |
| ContentFileId | uniqueidentifier | yes | FK (TenantId, ContentFileId) -> FileAssets(TenantId, Id) tenant-scoped |
| ExternalUrl | nvarchar(1000) | yes |  |
| DurationSeconds | int | yes |  |
| SortOrder | int | no | default 0 |
| IsFreePreview | bit | no | default 0 |
| PayPerLessonPrice | decimal(18,2) | yes |  |
| PrerequisiteLessonId | uniqueidentifier | yes | FK (TenantId, PrerequisiteLessonId) -> Lessons(TenantId, Id) tenant-scoped |
| DripUnlockAtUtc | datetime2(7) | yes |  |
| DripDaysAfterEnrollment | int | yes |  |
| IsPublished | bit | no | default 0 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: index (SectionId, SortOrder).

### LessonResources

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| LessonId | uniqueidentifier | no | FK (TenantId, LessonId) -> Lessons(TenantId, Id) tenant-scoped |
| Title | nvarchar(250) | no |  |
| FileId | uniqueidentifier | no | FK (TenantId, FileId) -> FileAssets(TenantId, Id) tenant-scoped |
| IsDownloadable | bit | no | default 1 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### LessonChapters

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| LessonId | uniqueidentifier | no | FK (TenantId, LessonId) -> Lessons(TenantId, Id) tenant-scoped |
| Title | nvarchar(250) | no |  |
| StartSeconds | int | no |  |
| Source | nvarchar(40) | no | one of: Manual, Ai; default Manual |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### LessonSlides

Flags: `TA`. Interactive slide deck content.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| LessonId | uniqueidentifier | no | FK (TenantId, LessonId) -> Lessons(TenantId, Id) tenant-scoped |
| SortOrder | int | no |  |
| ContentJson | nvarchar(max) | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### InteractiveVideoQuizzes

Flags: `TA`. Pauses the player at TimestampSeconds until the question is answered.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| LessonId | uniqueidentifier | no | FK (TenantId, LessonId) -> Lessons(TenantId, Id) tenant-scoped |
| QuestionId | uniqueidentifier | no | FK (TenantId, QuestionId) -> Questions(TenantId, Id) tenant-scoped |
| TimestampSeconds | int | no |  |
| IsRequired | bit | no | default 1 |
| AllowSkipAfterAttempts | int | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: index (LessonId, TimestampSeconds).

### LearningPaths

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| Title | nvarchar(250) | no |  |
| Slug | nvarchar(250) | no |  |
| Description | nvarchar(max) | yes |  |
| ThumbnailFileId | uniqueidentifier | yes | FK (TenantId, ThumbnailFileId) -> FileAssets(TenantId, Id) tenant-scoped |
| Status | nvarchar(40) | no | one of: Draft, Published, Archived; default Draft |
| Price | decimal(18,2) | no | default 0 |
| Currency | nvarchar(3) | no | default 'EGP' |
| IssuesCertificate | bit | no | default 1 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: unique (TenantId, Slug).

### LearningPathCourses

Flags: `TA`. Mandatory sequential ordering is enforced in the application layer.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| LearningPathId | uniqueidentifier | no | FK (TenantId, LearningPathId) -> LearningPaths(TenantId, Id) tenant-scoped |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| SortOrder | int | no |  |
| IsMandatory | bit | no | default 1 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (LearningPathId, CourseId); unique (LearningPathId, SortOrder).

## Learning Progress

### Enrollments

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| Source | nvarchar(40) | no | one of: Free, Purchase, Voucher, Wallet, Subscription, Admin, Path |
| Status | nvarchar(40) | no | one of: Active, Completed, Expired, Revoked; default Active |
| EnrolledAtUtc | datetime2(7) | no |  |
| ExpiresAtUtc | datetime2(7) | yes |  |
| CompletedAtUtc | datetime2(7) | yes |  |
| ProgressPercent | float | no | default 0 |
| OrderItemId | uniqueidentifier | yes | FK (TenantId, OrderItemId) -> OrderItems(TenantId, Id) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: unique (UserId, CourseId); index (CourseId, Status).

### LearningPathEnrollments

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| LearningPathId | uniqueidentifier | no | FK (TenantId, LearningPathId) -> LearningPaths(TenantId, Id) tenant-scoped |
| Status | nvarchar(40) | no | one of: Active, Completed, Revoked; default Active |
| EnrolledAtUtc | datetime2(7) | no |  |
| CompletedAtUtc | datetime2(7) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: unique (UserId, LearningPathId).

### LessonAccessGrants

Flags: `TA`. Pay-per-lesson unlocks without a course enrollment.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| LessonId | uniqueidentifier | no | FK (TenantId, LessonId) -> Lessons(TenantId, Id) tenant-scoped |
| Source | nvarchar(40) | no | one of: PayPerLesson, Voucher, Wallet, Admin |
| UnlockedAtUtc | datetime2(7) | no |  |
| ExpiresAtUtc | datetime2(7) | yes |  |
| OrderItemId | uniqueidentifier | yes | FK (TenantId, OrderItemId) -> OrderItems(TenantId, Id) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (UserId, LessonId).

### LessonProgress

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| LessonId | uniqueidentifier | no | FK (TenantId, LessonId) -> Lessons(TenantId, Id) tenant-scoped |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| Status | nvarchar(40) | no | one of: NotStarted, InProgress, Completed; default NotStarted |
| LastPositionSeconds | int | no | default 0 |
| WatchedSeconds | int | no | default 0 |
| PlaybackSpeed | float | yes |  |
| CompletedAtUtc | datetime2(7) | yes |  |
| LastAccessedAtUtc | datetime2(7) | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (UserId, LessonId); index (CourseId, UserId).

### LessonNotes

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| LessonId | uniqueidentifier | no | FK (TenantId, LessonId) -> Lessons(TenantId, Id) tenant-scoped |
| TimestampSeconds | int | yes |  |
| Body | nvarchar(4000) | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

### LessonBookmarks

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| LessonId | uniqueidentifier | no | FK (TenantId, LessonId) -> Lessons(TenantId, Id) tenant-scoped |
| TimestampSeconds | int | no |  |
| Label | nvarchar(200) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

### InteractiveQuizResponses

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| InteractiveVideoQuizId | uniqueidentifier | no | FK (TenantId, InteractiveVideoQuizId) -> InteractiveVideoQuizzes(TenantId, Id) tenant-scoped |
| AnswerJson | nvarchar(max) | no |  |
| IsCorrect | bit | no |  |
| AnsweredAtUtc | datetime2(7) | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### StudySessions

Flags: `T`. Feeds weekly study hours (parent portal), streaks and risk scoring.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| CourseId | uniqueidentifier | yes | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| LessonId | uniqueidentifier | yes | FK (TenantId, LessonId) -> Lessons(TenantId, Id) tenant-scoped |
| StartedAtUtc | datetime2(7) | no |  |
| DurationSeconds | int | no |  |

Indexes: index (UserId, StartedAtUtc).

## Assessments

### QuestionBanks

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| CourseId | uniqueidentifier | yes | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| Name | nvarchar(250) | no |  |
| Topic | nvarchar(200) | yes |  |
| Description | nvarchar(1000) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

### Questions

Flags: `TAS`. DataJson holds accepted answers for FillBlank/ShortAnswer, pairs for Matching, and an optional rubric for Essay.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| QuestionBankId | uniqueidentifier | no | FK (TenantId, QuestionBankId) -> QuestionBanks(TenantId, Id) tenant-scoped |
| QuestionType | nvarchar(40) | no | one of: McqSingle, McqMulti, TrueFalse, FillBlank, Matching, ShortAnswer, Essay |
| Text | nvarchar(max) | no |  |
| Difficulty | nvarchar(40) | no | one of: Easy, Medium, Hard; default Medium |
| Topic | nvarchar(200) | yes |  |
| DefaultPoints | float | no | default 1 |
| Explanation | nvarchar(max) | yes |  |
| DataJson | nvarchar(max) | yes |  |
| Source | nvarchar(40) | no | one of: Manual, Ai; default Manual |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: index (QuestionBankId, Difficulty).

### QuestionOptions

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| QuestionId | uniqueidentifier | no | FK (TenantId, QuestionId) -> Questions(TenantId, Id) tenant-scoped |
| Text | nvarchar(max) | no |  |
| IsCorrect | bit | no | default 0 |
| SortOrder | int | no | default 0 |
| MatchKey | nvarchar(100) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### Exams

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| SectionId | uniqueidentifier | yes | FK (TenantId, SectionId) -> Sections(TenantId, Id) tenant-scoped |
| Title | nvarchar(250) | no |  |
| Kind | nvarchar(40) | no | one of: Quiz, Exam |
| Instructions | nvarchar(max) | yes |  |
| DurationMinutes | int | yes |  |
| MaxAttempts | int | no | default 1 |
| PassingScorePercent | float | no | default 50 |
| ShuffleQuestions | bit | no | default 0 |
| ShuffleOptions | bit | no | default 0 |
| AvailableFromUtc | datetime2(7) | yes |  |
| AvailableUntilUtc | datetime2(7) | yes |  |
| TabSwitchDetection | bit | no | default 0 |
| MaxTabSwitches | int | yes |  |
| RequirePrivacyScreen | bit | no | default 0 |
| IsPublished | bit | no | default 0 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

### ExamQuestionRules

Flags: `TA`. Either a fixed question (QuestionId) or a random pool (QuestionBankId + PickCount + filters).

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| ExamId | uniqueidentifier | no | FK (TenantId, ExamId) -> Exams(TenantId, Id) tenant-scoped |
| QuestionId | uniqueidentifier | yes | FK (TenantId, QuestionId) -> Questions(TenantId, Id) tenant-scoped |
| QuestionBankId | uniqueidentifier | yes | FK (TenantId, QuestionBankId) -> QuestionBanks(TenantId, Id) tenant-scoped |
| PickCount | int | yes |  |
| Difficulty | nvarchar(40) | yes | one of: Easy, Medium, Hard |
| Topic | nvarchar(200) | yes |  |
| Points | float | yes |  |
| SortOrder | int | no | default 0 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### ExamAttempts

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| ExamId | uniqueidentifier | no | FK (TenantId, ExamId) -> Exams(TenantId, Id) tenant-scoped |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| AttemptNumber | int | no |  |
| StartedAtUtc | datetime2(7) | no |  |
| ExpiresAtUtc | datetime2(7) | yes |  |
| SubmittedAtUtc | datetime2(7) | yes |  |
| Status | nvarchar(40) | no | one of: InProgress, Submitted, Graded, Expired, Voided; default InProgress |
| Score | float | yes |  |
| MaxScore | float | yes |  |
| Passed | bit | yes |  |
| TabSwitchCount | int | no | default 0 |
| DeviceId | uniqueidentifier | yes | FK (UserId, DeviceId) -> UserDevices(UserId, Id) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (ExamId, UserId, AttemptNumber).

### ExamAttemptQuestions

Flags: `TA`. Frozen set and order of questions for one attempt.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| ExamAttemptId | uniqueidentifier | no | FK (TenantId, ExamAttemptId) -> ExamAttempts(TenantId, Id) tenant-scoped |
| QuestionId | uniqueidentifier | no | FK (TenantId, QuestionId) -> Questions(TenantId, Id) tenant-scoped |
| SortOrder | int | no |  |
| Points | float | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### ExamAnswers

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| ExamAttemptQuestionId | uniqueidentifier | no | FK (TenantId, ExamAttemptQuestionId) -> ExamAttemptQuestions(TenantId, Id) tenant-scoped |
| AnswerJson | nvarchar(max) | yes |  |
| IsCorrect | bit | yes |  |
| PointsAwarded | float | yes |  |
| GradedBy | uniqueidentifier | yes | FK (TenantId, GradedBy) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| GradedAtUtc | datetime2(7) | yes |  |
| Feedback | nvarchar(2000) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (ExamAttemptQuestionId).

### ProctoringEvents

Flags: `T`. Signals for human review. Not proof of cheating (see docs/SECURITY_LIMITS.md).

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| ExamAttemptId | uniqueidentifier | no | FK (TenantId, ExamAttemptId) -> ExamAttempts(TenantId, Id) tenant-scoped |
| EventType | nvarchar(40) | no | one of: TabSwitch, WindowBlur, FullscreenExit, CopyPaste, NetworkLoss, Other |
| OccurredAtUtc | datetime2(7) | no |  |
| DetailsJson | nvarchar(max) | yes |  |

Indexes: index (ExamAttemptId, OccurredAtUtc).

## Assignments

### Assignments

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| LessonId | uniqueidentifier | yes | FK (TenantId, LessonId) -> Lessons(TenantId, Id) tenant-scoped |
| Title | nvarchar(250) | no |  |
| Instructions | nvarchar(max) | yes |  |
| DueAtUtc | datetime2(7) | yes |  |
| MaxPoints | float | no |  |
| AllowResubmission | bit | no | default 0 |
| MaxResubmissions | int | yes |  |
| IsGroup | bit | no | default 0 |
| MaxGroupSize | int | yes |  |
| PeerReviewEnabled | bit | no | default 0 |
| PeerReviewsPerSubmission | int | yes |  |
| PeerReviewDueAtUtc | datetime2(7) | yes |  |
| PlagiarismCheckEnabled | bit | no | default 0 |
| AiProvisionalGradingEnabled | bit | no | default 0 |
| IsPublished | bit | no | default 0 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

### AssignmentRubricCriteria

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| AssignmentId | uniqueidentifier | no | FK (TenantId, AssignmentId) -> Assignments(TenantId, Id) tenant-scoped |
| Name | nvarchar(200) | no |  |
| Description | nvarchar(1000) | yes |  |
| MaxPoints | float | no |  |
| SortOrder | int | no | default 0 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### AssignmentGroups

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| AssignmentId | uniqueidentifier | no | FK (TenantId, AssignmentId) -> Assignments(TenantId, Id) tenant-scoped |
| Name | nvarchar(200) | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### AssignmentGroupMembers

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| AssignmentGroupId | uniqueidentifier | no | FK (TenantId, AssignmentGroupId) -> AssignmentGroups(TenantId, Id) tenant-scoped |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (AssignmentGroupId, UserId).

### Submissions

Flags: `TAS`. Exactly one of UserId (individual) or AssignmentGroupId (team) is set. Enforced by CHECK. Unique version per submitter via two filtered indexes.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| AssignmentId | uniqueidentifier | no | FK (TenantId, AssignmentId) -> Assignments(TenantId, Id) tenant-scoped |
| UserId | uniqueidentifier | yes | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| AssignmentGroupId | uniqueidentifier | yes | FK (TenantId, AssignmentGroupId) -> AssignmentGroups(TenantId, Id) tenant-scoped |
| Version | int | no | default 1 |
| TextContent | nvarchar(max) | yes |  |
| LinkUrl | nvarchar(1000) | yes |  |
| SubmittedAtUtc | datetime2(7) | no |  |
| IsLate | bit | no | default 0 |
| Status | nvarchar(40) | no | one of: Submitted, UnderReview, Graded, ReturnedForResubmission; default Submitted |
| TotalScore | float | yes |  |
| GradedBy | uniqueidentifier | yes | FK (TenantId, GradedBy) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| GradedAtUtc | datetime2(7) | yes |  |
| InstructorFeedback | nvarchar(max) | yes |  |
| AiProvisionalScore | float | yes |  |
| AiFeedback | nvarchar(max) | yes |  |
| MaxSimilarity | float | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

### SubmissionFiles

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| SubmissionId | uniqueidentifier | no | FK (TenantId, SubmissionId) -> Submissions(TenantId, Id) tenant-scoped |
| FileId | uniqueidentifier | no | FK (TenantId, FileId) -> FileAssets(TenantId, Id) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### SubmissionCriterionScores

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| SubmissionId | uniqueidentifier | no | FK (TenantId, SubmissionId) -> Submissions(TenantId, Id) tenant-scoped |
| CriterionId | uniqueidentifier | no | FK (TenantId, CriterionId) -> AssignmentRubricCriteria(TenantId, Id) tenant-scoped |
| Points | float | no |  |
| Feedback | nvarchar(2000) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (SubmissionId, CriterionId).

### SubmissionComments

Flags: `TAS`. Inline instructor feedback anchored to a text range or file page via AnchorJson.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| SubmissionId | uniqueidentifier | no | FK (TenantId, SubmissionId) -> Submissions(TenantId, Id) tenant-scoped |
| AuthorId | uniqueidentifier | no | FK (TenantId, AuthorId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| AnchorJson | nvarchar(max) | yes |  |
| Body | nvarchar(4000) | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

### PeerReviews

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| SubmissionId | uniqueidentifier | no | FK (TenantId, SubmissionId) -> Submissions(TenantId, Id) tenant-scoped |
| ReviewerId | uniqueidentifier | no | FK (TenantId, ReviewerId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| Status | nvarchar(40) | no | one of: Assigned, Submitted, Skipped; default Assigned |
| TotalScore | float | yes |  |
| Comments | nvarchar(4000) | yes |  |
| SubmittedAtUtc | datetime2(7) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (SubmissionId, ReviewerId).

### PeerReviewScores

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| PeerReviewId | uniqueidentifier | no | FK (TenantId, PeerReviewId) -> PeerReviews(TenantId, Id) tenant-scoped |
| CriterionId | uniqueidentifier | no | FK (TenantId, CriterionId) -> AssignmentRubricCriteria(TenantId, Id) tenant-scoped |
| Points | float | no |  |
| Comment | nvarchar(1000) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### PlagiarismChecks

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| SubmissionId | uniqueidentifier | no | FK (TenantId, SubmissionId) -> Submissions(TenantId, Id) tenant-scoped |
| BackgroundJobId | uniqueidentifier | yes | FK (TenantId, BackgroundJobId) -> BackgroundJobs(TenantId, Id) tenant-scoped |
| Status | nvarchar(40) | no | one of: Pending, Processing, Completed, Failed; default Pending |
| MaxSimilarity | float | yes |  |
| CheckedAtUtc | datetime2(7) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### PlagiarismMatches

Flags: `TA`. Similarity is a signal; an instructor sets the outcome.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| PlagiarismCheckId | uniqueidentifier | no | FK (TenantId, PlagiarismCheckId) -> PlagiarismChecks(TenantId, Id) tenant-scoped |
| OtherSubmissionId | uniqueidentifier | no | FK (TenantId, OtherSubmissionId) -> Submissions(TenantId, Id) tenant-scoped |
| Similarity | float | no |  |
| ReviewedBy | uniqueidentifier | yes | FK (TenantId, ReviewedBy) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| ReviewOutcome | nvarchar(40) | no | one of: Unreviewed, Dismissed, Confirmed; default Unreviewed |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

## Community & Live

### DiscussionThreads

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| LessonId | uniqueidentifier | yes | FK (TenantId, LessonId) -> Lessons(TenantId, Id) tenant-scoped |
| AuthorId | uniqueidentifier | no | FK (TenantId, AuthorId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| ThreadType | nvarchar(40) | no | one of: LessonQA, Forum |
| Title | nvarchar(300) | no |  |
| Body | nvarchar(max) | no |  |
| IsPinned | bit | no | default 0 |
| IsLocked | bit | no | default 0 |
| UpvoteCount | int | no | default 0 |
| AcceptedPostId | uniqueidentifier | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: index (CourseId, LessonId).

### DiscussionPosts

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| ThreadId | uniqueidentifier | no | FK (TenantId, ThreadId) -> DiscussionThreads(TenantId, Id) tenant-scoped |
| AuthorId | uniqueidentifier | no | FK (TenantId, AuthorId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| ParentPostId | uniqueidentifier | yes | FK (TenantId, ParentPostId) -> DiscussionPosts(TenantId, Id) tenant-scoped |
| Body | nvarchar(max) | no |  |
| IsInstructorPinned | bit | no | default 0 |
| UpvoteCount | int | no | default 0 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

### DiscussionVotes

Flags: `TA`. CHECK: exactly one of ThreadId / PostId. Unique per user and target.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| ThreadId | uniqueidentifier | yes | FK (TenantId, ThreadId) -> DiscussionThreads(TenantId, Id) tenant-scoped |
| PostId | uniqueidentifier | yes | FK (TenantId, PostId) -> DiscussionPosts(TenantId, Id) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### CourseReviews

Flags: `TA`. One review per student per course; only enrolled students may post. CHECK: Rating between 1 and 5. Hidden = removed by moderation.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| Rating | int | no |  |
| Body | nvarchar(2000) | yes |  |
| Status | nvarchar(40) | no | one of: Published, Hidden; default Published |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (TenantId, UserId, CourseId).

### CourseReviewReports

Flags: `TA`. A user can report a review once; moderation resolves open reports.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| ReviewId | uniqueidentifier | no | FK (TenantId, ReviewId) -> CourseReviews(TenantId, Id) tenant-scoped |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| Reason | nvarchar(1000) | no |  |
| Status | nvarchar(40) | no | one of: Open, Dismissed, Actioned; default Open |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (TenantId, ReviewId, UserId).

### ChatRooms

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| CourseId | uniqueidentifier | yes | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| RoomType | nvarchar(40) | no | one of: Course, Direct, Group |
| Name | nvarchar(200) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### ChatRoomMembers

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| ChatRoomId | uniqueidentifier | no | FK (TenantId, ChatRoomId) -> ChatRooms(TenantId, Id) tenant-scoped |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| JoinedAtUtc | datetime2(7) | no |  |
| LastReadAtUtc | datetime2(7) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (ChatRoomId, UserId).

### ChatMessages

Flags: `TS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| ChatRoomId | uniqueidentifier | no | FK (TenantId, ChatRoomId) -> ChatRooms(TenantId, Id) tenant-scoped |
| SenderId | uniqueidentifier | no | FK (TenantId, SenderId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| Body | nvarchar(4000) | no |  |
| SentAtUtc | datetime2(7) | no |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: index (ChatRoomId, SentAtUtc).

### Announcements

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| CourseId | uniqueidentifier | yes | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| AuthorId | uniqueidentifier | no | FK (TenantId, AuthorId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| Title | nvarchar(250) | no |  |
| Body | nvarchar(max) | no |  |
| PublishedAtUtc | datetime2(7) | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

### Notifications

Flags: `T`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| Type | nvarchar(60) | no |  |
| Title | nvarchar(250) | no |  |
| Body | nvarchar(2000) | yes |  |
| DataJson | nvarchar(max) | yes |  |
| IsRead | bit | no | default 0 |
| ReadAtUtc | datetime2(7) | yes |  |
| CreatedAtUtc | datetime2(7) | no |  |

Indexes: index (UserId, IsRead).

### PushTokens

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| DeviceId | uniqueidentifier | yes | FK (UserId, DeviceId) -> UserDevices(UserId, Id) tenant-scoped |
| FcmToken | nvarchar(500) | no |  |
| Platform | nvarchar(40) | no | one of: Android, Ios, Web |
| LastSeenAtUtc | datetime2(7) | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (UserId, FcmToken).

### LiveSessions

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| HostId | uniqueidentifier | no | FK (TenantId, HostId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| Title | nvarchar(250) | no |  |
| Provider | nvarchar(40) | no | one of: Zoom, GoogleMeet, Jitsi, Other |
| JoinUrl | nvarchar(1000) | no |  |
| StartsAtUtc | datetime2(7) | no |  |
| EndsAtUtc | datetime2(7) | yes |  |
| Status | nvarchar(40) | no | one of: Scheduled, Live, Ended, Cancelled; default Scheduled |
| ReminderSentAtUtc | datetime2(7) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

### LiveSessionAttendance

Flags: `TA`. JoinLink attendance means the student opened our join link, not that they stayed engaged.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| LiveSessionId | uniqueidentifier | no | FK (TenantId, LiveSessionId) -> LiveSessions(TenantId, Id) tenant-scoped |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| JoinedAtUtc | datetime2(7) | no |  |
| LeftAtUtc | datetime2(7) | yes |  |
| DurationSeconds | int | yes |  |
| Source | nvarchar(40) | no | one of: JoinLink, Provider, Manual; default JoinLink |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (LiveSessionId, UserId).

## Gamification, Parents & Certificates

### XpTransactions

Flags: `T`. Append-only. Leaderboards (course and global) are queries over this table, cached by the analytics job.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| Points | int | no |  |
| Reason | nvarchar(100) | no |  |
| SourceType | nvarchar(60) | yes |  |
| SourceId | uniqueidentifier | yes |  |
| AwardedAtUtc | datetime2(7) | no |  |

Indexes: index (UserId, AwardedAtUtc).

### UserStreaks

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| CurrentStreakDays | int | no | default 0 |
| LongestStreakDays | int | no | default 0 |
| LastActivityDate | date | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (UserId).

### Badges

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| Code | nvarchar(60) | no |  |
| Name | nvarchar(200) | no |  |
| Description | nvarchar(500) | yes |  |
| IconUrl | nvarchar(500) | yes |  |
| RuleJson | nvarchar(max) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: unique (TenantId, Code).

### UserBadges

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| BadgeId | uniqueidentifier | no | FK (TenantId, BadgeId) -> Badges(TenantId, Id) tenant-scoped |
| AwardedAtUtc | datetime2(7) | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (UserId, BadgeId).

### ParentStudentLinks

Flags: `TA`. A parent sees a student only while Status = Active. Approval by the student, an instructor or an admin is a tenant policy.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| ParentUserId | uniqueidentifier | no | FK (TenantId, ParentUserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| StudentUserId | uniqueidentifier | no | FK (TenantId, StudentUserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| Status | nvarchar(40) | no | one of: Pending, Active, Revoked; default Pending |
| LinkCodeHash | nvarchar(128) | yes |  |
| ApprovedBy | uniqueidentifier | yes | FK (TenantId, ApprovedBy) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| ApprovedAtUtc | datetime2(7) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (ParentUserId, StudentUserId).

### ParentAlerts

Flags: `T`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| ParentUserId | uniqueidentifier | no | FK (TenantId, ParentUserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| StudentUserId | uniqueidentifier | no | FK (TenantId, StudentUserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| AlertType | nvarchar(40) | no | one of: LowQuizScore, LowAttendance, Inactivity, OverdueAssignment, HighDropoutRisk |
| Message | nvarchar(1000) | no |  |
| CreatedAtUtc | datetime2(7) | no |  |
| ReadAtUtc | datetime2(7) | yes |  |

Indexes: index (ParentUserId, ReadAtUtc).

### Certificates

Flags: `TAS`. CHECK: exactly one of CourseId / LearningPathId. VerificationCode is globally unique for the public verify endpoint.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| CourseId | uniqueidentifier | yes | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| LearningPathId | uniqueidentifier | yes | FK (TenantId, LearningPathId) -> LearningPaths(TenantId, Id) tenant-scoped |
| VerificationCode | nvarchar(32) | no | unique |
| IssuedAtUtc | datetime2(7) | no |  |
| PdfFileId | uniqueidentifier | yes | FK (TenantId, PdfFileId) -> FileAssets(TenantId, Id) tenant-scoped |
| RevokedAtUtc | datetime2(7) | yes |  |
| RevokedReason | nvarchar(500) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

## Commerce

### Bundles

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| Name | nvarchar(250) | no |  |
| Description | nvarchar(max) | yes |  |
| Price | decimal(18,2) | no |  |
| Currency | nvarchar(3) | no | default 'EGP' |
| IsActive | bit | no | default 1 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

### BundleCourses

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| BundleId | uniqueidentifier | no | FK (TenantId, BundleId) -> Bundles(TenantId, Id) tenant-scoped |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (BundleId, CourseId).

### SubscriptionPlans

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| Name | nvarchar(200) | no |  |
| Description | nvarchar(1000) | yes |  |
| Price | decimal(18,2) | no |  |
| Currency | nvarchar(3) | no | default 'EGP' |
| BillingPeriod | nvarchar(40) | no | one of: Monthly, Quarterly, Yearly |
| Scope | nvarchar(40) | no | one of: AllCourses, SelectedCourses |
| IsActive | bit | no | default 1 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

### SubscriptionPlanCourses

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| PlanId | uniqueidentifier | no | FK (TenantId, PlanId) -> SubscriptionPlans(TenantId, Id) tenant-scoped |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (PlanId, CourseId).

### UserSubscriptions

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| PlanId | uniqueidentifier | no | FK (TenantId, PlanId) -> SubscriptionPlans(TenantId, Id) tenant-scoped |
| Status | nvarchar(40) | no | one of: Active, PastDue, Cancelled, Expired |
| StartedAtUtc | datetime2(7) | no |  |
| CurrentPeriodEndUtc | datetime2(7) | no |  |
| CancelledAtUtc | datetime2(7) | yes |  |
| GatewayReference | nvarchar(200) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: index (UserId, Status).

### CartItems

Flags: `TA`. CHECK: exactly one target column matches ItemType.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| ItemType | nvarchar(40) | no | one of: Course, Lesson, Bundle, Plan, Path |
| CourseId | uniqueidentifier | yes | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| LessonId | uniqueidentifier | yes | FK (TenantId, LessonId) -> Lessons(TenantId, Id) tenant-scoped |
| BundleId | uniqueidentifier | yes | FK (TenantId, BundleId) -> Bundles(TenantId, Id) tenant-scoped |
| PlanId | uniqueidentifier | yes | FK (TenantId, PlanId) -> SubscriptionPlans(TenantId, Id) tenant-scoped |
| LearningPathId | uniqueidentifier | yes | FK (TenantId, LearningPathId) -> LearningPaths(TenantId, Id) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### WishlistItems

Flags: `TA`. Student wishlist: courses saved for later, one row per student-course pair.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (TenantId, UserId, CourseId).

### Coupons

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| Code | nvarchar(50) | no |  |
| DiscountType | nvarchar(40) | no | one of: Percentage, Fixed |
| Value | decimal(18,2) | no |  |
| MaxRedemptions | int | yes |  |
| MaxPerUser | int | no | default 1 |
| MinOrderAmount | decimal(18,2) | yes |  |
| ExpiresAtUtc | datetime2(7) | yes |  |
| CourseId | uniqueidentifier | yes | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| IsActive | bit | no | default 1 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: unique (TenantId, Code).

### Orders

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| Status | nvarchar(40) | no | one of: Pending, AwaitingManualReview, Paid, Failed, Cancelled, Refunded; default Pending |
| Subtotal | decimal(18,2) | no |  |
| DiscountTotal | decimal(18,2) | no | default 0 |
| Total | decimal(18,2) | no |  |
| Currency | nvarchar(3) | no |  |
| CouponId | uniqueidentifier | yes | FK (TenantId, CouponId) -> Coupons(TenantId, Id) tenant-scoped |
| PaymentMethod | nvarchar(40) | no | one of: Gateway, Wallet, Voucher, Manual, Free |
| PaidAtUtc | datetime2(7) | yes |  |
| ReferralCode | nvarchar(50) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: index (UserId, Status).

### OrderItems

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| OrderId | uniqueidentifier | no | FK (TenantId, OrderId) -> Orders(TenantId, Id) tenant-scoped |
| ItemType | nvarchar(40) | no | one of: Course, Lesson, Bundle, Plan, Path, WalletTopUp |
| CourseId | uniqueidentifier | yes | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| LessonId | uniqueidentifier | yes | FK (TenantId, LessonId) -> Lessons(TenantId, Id) tenant-scoped |
| BundleId | uniqueidentifier | yes | FK (TenantId, BundleId) -> Bundles(TenantId, Id) tenant-scoped |
| PlanId | uniqueidentifier | yes | FK (TenantId, PlanId) -> SubscriptionPlans(TenantId, Id) tenant-scoped |
| LearningPathId | uniqueidentifier | yes | FK (TenantId, LearningPathId) -> LearningPaths(TenantId, Id) tenant-scoped |
| Description | nvarchar(300) | no |  |
| UnitPrice | decimal(18,2) | no |  |
| Quantity | int | no | default 1 |
| LineTotal | decimal(18,2) | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### CouponRedemptions

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| CouponId | uniqueidentifier | no | FK (TenantId, CouponId) -> Coupons(TenantId, Id) tenant-scoped |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| OrderId | uniqueidentifier | no | FK (TenantId, OrderId) -> Orders(TenantId, Id) tenant-scoped |
| RedeemedAtUtc | datetime2(7) | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (CouponId, OrderId).

### Payments

Flags: `T`. Raw gateway payloads are not stored (they can carry card data). Store only reference and status.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| OrderId | uniqueidentifier | no | FK (TenantId, OrderId) -> Orders(TenantId, Id) tenant-scoped |
| Method | nvarchar(40) | no | one of: Paymob, Fawry, Stripe, Wallet, Manual, Voucher |
| GatewayReference | nvarchar(200) | yes |  |
| Amount | decimal(18,2) | no |  |
| Currency | nvarchar(3) | no |  |
| Status | nvarchar(40) | no | one of: Pending, Succeeded, Failed, Refunded |
| FailureReason | nvarchar(500) | yes |  |
| CreatedAtUtc | datetime2(7) | no |  |
| ConfirmedAtUtc | datetime2(7) | yes |  |
| IdempotencyKey | nvarchar(100) | no | unique |

Indexes: index (GatewayReference).

### ManualPaymentSubmissions

Flags: `TAS`. Admin review queue. Approval writes the order/wallet change and an audit row in one transaction.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| OrderId | uniqueidentifier | yes | FK (TenantId, OrderId) -> Orders(TenantId, Id) tenant-scoped |
| Purpose | nvarchar(40) | no | one of: Order, WalletTopUp |
| Method | nvarchar(40) | no | one of: VodafoneCash, InstaPay, BankTransfer |
| Amount | decimal(18,2) | no |  |
| SenderReference | nvarchar(200) | yes |  |
| SenderPhoneOrAccount | nvarchar(100) | yes |  |
| TransferredAtUtc | datetime2(7) | yes |  |
| ReceiptFileId | uniqueidentifier | no | FK (TenantId, ReceiptFileId) -> FileAssets(TenantId, Id) tenant-scoped |
| Status | nvarchar(40) | no | one of: Pending, Approved, Rejected; default Pending |
| ReviewedBy | uniqueidentifier | yes | FK (TenantId, ReviewedBy) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| ReviewedAtUtc | datetime2(7) | yes |  |
| RejectionReason | nvarchar(500) | yes |  |
| WalletTransactionId | uniqueidentifier | yes | FK (TenantId, WalletTransactionId) -> WalletTransactions(TenantId, Id) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: index (Status, TenantId).

### Invoices

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| OrderId | uniqueidentifier | no | FK (TenantId, OrderId) -> Orders(TenantId, Id) tenant-scoped |
| InvoiceNumber | nvarchar(40) | no |  |
| IssuedAtUtc | datetime2(7) | no |  |
| PdfFileId | uniqueidentifier | yes | FK (TenantId, PdfFileId) -> FileAssets(TenantId, Id) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: unique (TenantId, InvoiceNumber).

### VoucherBatches

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| Name | nvarchar(200) | no |  |
| Prefix | nvarchar(12) | yes |  |
| Quantity | int | no |  |
| VoucherType | nvarchar(40) | no | one of: CourseAccess, WalletCredit |
| CourseId | uniqueidentifier | yes | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| WalletAmount | decimal(18,2) | yes |  |
| ExpiresAtUtc | datetime2(7) | yes |  |
| BackgroundJobId | uniqueidentifier | yes | FK (TenantId, BackgroundJobId) -> BackgroundJobs(TenantId, Id) tenant-scoped |
| ExportFileId | uniqueidentifier | yes | FK (TenantId, ExportFileId) -> FileAssets(TenantId, Id) tenant-scoped |
| Status | nvarchar(40) | no | one of: Pending, Generating, Ready, Failed; default Pending |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

### Vouchers

Flags: `TA`. Only the SHA-256 of the code is stored. Plain codes exist once, in the CSV produced when the batch is generated.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| VoucherBatchId | uniqueidentifier | no | FK (TenantId, VoucherBatchId) -> VoucherBatches(TenantId, Id) tenant-scoped |
| CodeHash | nvarchar(64) | no |  |
| CodeLast4 | nvarchar(4) | no |  |
| Status | nvarchar(40) | no | one of: Unused, Redeemed, Void; default Unused |
| RedeemedBy | uniqueidentifier | yes | FK (TenantId, RedeemedBy) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| RedeemedAtUtc | datetime2(7) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (TenantId, CodeHash); index (VoucherBatchId, Status).

### Wallets

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| Balance | decimal(18,2) | no | default 0 |
| Currency | nvarchar(3) | no | default 'EGP' |
| RowVersion | rowversion | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (UserId).

### WalletTransactions

Flags: `T`. Append-only ledger. Amount is signed. Balance changes only through this table, inside a transaction with the wallet row's RowVersion check.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| WalletId | uniqueidentifier | no | FK (TenantId, WalletId) -> Wallets(TenantId, Id) tenant-scoped |
| Type | nvarchar(40) | no | one of: TopUpGateway, TopUpVoucher, TopUpManual, Purchase, Refund, AffiliateCommission, Adjustment |
| Amount | decimal(18,2) | no |  |
| BalanceAfter | decimal(18,2) | no |  |
| ReferenceType | nvarchar(60) | yes |  |
| ReferenceId | uniqueidentifier | yes |  |
| Description | nvarchar(300) | yes |  |
| IdempotencyKey | nvarchar(100) | no |  |
| CreatedAtUtc | datetime2(7) | no |  |
| CreatedBy | uniqueidentifier | yes | FK (TenantId, CreatedBy) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |

Indexes: unique (WalletId, IdempotencyKey); index (WalletId, CreatedAtUtc).

### AffiliateProfiles

Flags: `TAS`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| ReferralCode | nvarchar(40) | no |  |
| CommissionPercent | float | no |  |
| IsActive | bit | no | default 1 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

Indexes: unique (TenantId, ReferralCode); unique (UserId).

### Referrals

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| AffiliateUserId | uniqueidentifier | no | FK (TenantId, AffiliateUserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| ReferredUserId | uniqueidentifier | no | FK (TenantId, ReferredUserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| ReferralCodeUsed | nvarchar(40) | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (ReferredUserId).

### AffiliateCommissions

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| AffiliateUserId | uniqueidentifier | no | FK (TenantId, AffiliateUserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| OrderId | uniqueidentifier | no | FK (TenantId, OrderId) -> Orders(TenantId, Id) tenant-scoped |
| Amount | decimal(18,2) | no |  |
| Status | nvarchar(40) | no | one of: Pending, Credited, Reversed; default Pending |
| WalletTransactionId | uniqueidentifier | yes | FK (TenantId, WalletTransactionId) -> WalletTransactions(TenantId, Id) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (AffiliateUserId, OrderId).

### InstructorRevenueShares

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| InstructorId | uniqueidentifier | no | FK (TenantId, InstructorId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| SharePercent | float | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (CourseId, InstructorId).

### CourseLearningWeights

Flags: `TA`. TenantAdmin-approved learning time of ALL non-video content of a course together (split over its non-video lessons), used only for subscription revenue allocation.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| NonVideoCourseSeconds | int | no |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (CourseId).

### OrderItemAllocations

Flags: `TA`. Snapshot taken at checkout for each course covered by the line: its list price (bundle and path weights) and, for subscription plans, the approved non-video learning time and the published non-video lesson count at purchase.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| OrderItemId | uniqueidentifier | no | FK (TenantId, OrderItemId) -> OrderItems(TenantId, Id) tenant-scoped |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| ListPrice | decimal(18,2) | no |  |
| NonVideoSeconds | int | yes |  |
| NonVideoLessonCount | int | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (OrderItemId, CourseId).

### OrderItemShares

Flags: `TA`. Snapshot taken at checkout: which instructors earn from each covered course and at what percentage. A null percentage means none was configured at purchase and the current setting is used when earnings are calculated.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| OrderItemId | uniqueidentifier | no | FK (TenantId, OrderItemId) -> OrderItems(TenantId, Id) tenant-scoped |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| InstructorId | uniqueidentifier | no | FK (TenantId, InstructorId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| SharePercent | float | yes |  |
| ReleasedLate | bit | no | default 0 |
| SettledPayoutId | uniqueidentifier | yes | FK (TenantId, SettledPayoutId) -> InstructorPayouts(TenantId, Id) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (OrderItemId, CourseId, InstructorId).

### InstructorPayouts

Flags: `TA`. Payout is recorded here; actual money transfer is outside the system.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| InstructorId | uniqueidentifier | no | FK (TenantId, InstructorId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| PeriodStartUtc | datetime2(7) | no |  |
| PeriodEndUtc | datetime2(7) | no |  |
| Amount | decimal(18,2) | no |  |
| Currency | nvarchar(3) | no |  |
| Status | nvarchar(40) | no | one of: Calculated, Approved, Paid, Cancelled; default Calculated |
| PaidAtUtc | datetime2(7) | yes |  |
| PaymentReference | nvarchar(200) | yes |  |
| ApprovedBy | uniqueidentifier | yes | FK (TenantId, ApprovedBy) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### InstructorPayoutLines

Flags: `T`. Immutable record of what each payout earned per order line and course: GrossEarned before refunds, PaidEarned after refunds that existed at calculation. Refund debits are computed from these rows, never from recomputed activity.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| InstructorPayoutId | uniqueidentifier | no | FK (TenantId, InstructorPayoutId) -> InstructorPayouts(TenantId, Id) tenant-scoped |
| OrderId | uniqueidentifier | no | FK (TenantId, OrderId) -> Orders(TenantId, Id) tenant-scoped |
| OrderItemId | uniqueidentifier | no | FK (TenantId, OrderItemId) -> OrderItems(TenantId, Id) tenant-scoped |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| GrossEarned | decimal(18,2) | no |  |
| PaidEarned | decimal(18,2) | no |  |

Indexes: index (InstructorPayoutId); index (OrderId).

### OrderRefunds

Flags: `T`. Accounting record of a refund decided and executed outside the system. It moves no money; it only reverses instructor earnings proportionally.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| OrderId | uniqueidentifier | no | FK (TenantId, OrderId) -> Orders(TenantId, Id) tenant-scoped |
| Reference | nvarchar(100) | no |  |
| Amount | decimal(18,2) | no |  |
| Reason | nvarchar(500) | yes |  |
| RecordedBy | uniqueidentifier | yes | FK (TenantId, RecordedBy) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no |  |

Indexes: unique (OrderId, Reference).

### InstructorDebits

Flags: `T`. Earnings already inside a calculated payout when their sale was refunded. Carried against the instructor's next payouts; never withdrawn automatically.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| InstructorId | uniqueidentifier | no | FK (TenantId, InstructorId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| OrderRefundId | uniqueidentifier | no | FK (TenantId, OrderRefundId) -> OrderRefunds(TenantId, Id) tenant-scoped |
| SourcePayoutId | uniqueidentifier | no | FK (TenantId, SourcePayoutId) -> InstructorPayouts(TenantId, Id) tenant-scoped |
| Amount | decimal(18,2) | no |  |
| RemainingAmount | decimal(18,2) | no |  |
| Currency | nvarchar(3) | no |  |
| CreatedAtUtc | datetime2(7) | no |  |

Indexes: index (InstructorId, RemainingAmount).

### InstructorDebitApplications

Flags: `T`. How much of a debit a payout absorbed. Cancelling the payout gives the amount back to the debit.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| InstructorDebitId | uniqueidentifier | no | FK (TenantId, InstructorDebitId) -> InstructorDebits(TenantId, Id) tenant-scoped |
| InstructorPayoutId | uniqueidentifier | no | FK (TenantId, InstructorPayoutId) -> InstructorPayouts(TenantId, Id) tenant-scoped |
| Amount | decimal(18,2) | no |  |

Indexes: index (InstructorPayoutId).

## Enterprise & Analytics

### AuditLogs

Flags: `N`. Append-only. Application DB user should have INSERT/SELECT only. Sensitive values (passwords, tokens) are never written.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | yes | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | yes | FK (UserId) -> AspNetUsers(Id) |
| Action | nvarchar(40) | no | one of: Create, Update, Delete, Restore, Approve, Reject, Generate, Login, ResetDevice, Other |
| EntityName | nvarchar(100) | no |  |
| EntityId | nvarchar(64) | yes |  |
| OldValuesJson | nvarchar(max) | yes |  |
| NewValuesJson | nvarchar(max) | yes |  |
| IpAddress | nvarchar(64) | yes |  |
| CorrelationId | nvarchar(64) | yes |  |
| OccurredAtUtc | datetime2(7) | no |  |

Indexes: index (TenantId, OccurredAtUtc); index (EntityName, EntityId).

### ApiKeys

Flags: `TA`. Full key shown once at creation; only the hash is stored.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| Name | nvarchar(200) | no |  |
| KeyPrefix | nvarchar(12) | no |  |
| KeyHash | nvarchar(128) | no | unique |
| ScopesJson | nvarchar(max) | no |  |
| ExpiresAtUtc | datetime2(7) | yes |  |
| LastUsedAtUtc | datetime2(7) | yes |  |
| RevokedAtUtc | datetime2(7) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

### WebhookEndpoints

Flags: `TAS`. SecretProtected uses ASP.NET Data Protection. Deliveries are signed with HMAC-SHA256.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| Url | nvarchar(1000) | no |  |
| SecretProtected | nvarchar(max) | no |  |
| EventTypesJson | nvarchar(max) | no |  |
| IsActive | bit | no | default 1 |
| ConsecutiveFailures | int | no | default 0 |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |
| IsDeleted | bit | no | default 0 |
| DeletedAtUtc | datetime2(7) | yes |  |
| DeletedBy | uniqueidentifier | yes |  |

### WebhookDeliveries

Flags: `TA`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| WebhookEndpointId | uniqueidentifier | no | FK (TenantId, WebhookEndpointId) -> WebhookEndpoints(TenantId, Id) tenant-scoped |
| EventType | nvarchar(100) | no |  |
| PayloadJson | nvarchar(max) | no |  |
| Status | nvarchar(40) | no | one of: Pending, Delivered, Failed, Abandoned; default Pending |
| AttemptCount | int | no | default 0 |
| NextAttemptAtUtc | datetime2(7) | yes |  |
| LastHttpStatus | int | yes |  |
| LastError | nvarchar(1000) | yes |  |
| DeliveredAtUtc | datetime2(7) | yes |  |
| BackgroundJobId | uniqueidentifier | yes | FK (TenantId, BackgroundJobId) -> BackgroundJobs(TenantId, Id) tenant-scoped |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: index (Status, NextAttemptAtUtc).

### LessonIndexStates

Flags: `TA`. Tracks what the AI service has embedded so ingest runs only when content changed.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| LessonId | uniqueidentifier | no | FK (TenantId, LessonId) -> Lessons(TenantId, Id) tenant-scoped |
| Status | nvarchar(40) | no | one of: NotIndexed, Pending, Processing, Indexed, Failed; default NotIndexed |
| ContentHash | nvarchar(64) | yes |  |
| ChunkCount | int | yes |  |
| IndexedAtUtc | datetime2(7) | yes |  |
| LastError | nvarchar(1000) | yes |  |
| CreatedAtUtc | datetime2(7) | no | default SYSUTCDATETIME() |
| CreatedBy | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetime2(7) | yes |  |
| UpdatedBy | uniqueidentifier | yes |  |

Indexes: unique (LessonId).

### AiUsageLogs

Flags: `T`. No prompt text stored. Used for rate limiting and cost review.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | yes | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| Endpoint | nvarchar(80) | no |  |
| CourseId | uniqueidentifier | yes | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| Provider | nvarchar(30) | yes |  |
| LatencyMs | int | yes |  |
| StatusCode | int | no |  |
| CreatedAtUtc | datetime2(7) | no |  |

Indexes: index (UserId, CreatedAtUtc).

### DropoutRiskScores

Flags: `T`

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| UserId | uniqueidentifier | no | FK (TenantId, UserId) -> UserTenantMemberships(TenantId, UserId) tenant-scoped |
| CourseId | uniqueidentifier | no | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| Score | float | no |  |
| Level | nvarchar(40) | no | one of: Low, Medium, High |
| FeaturesJson | nvarchar(max) | no |  |
| ComputedAtUtc | datetime2(7) | no |  |

Indexes: index (CourseId, UserId, ComputedAtUtc).

### AnalyticsSnapshots

Flags: `T`. Pre-computed dashboards, drop-off heatmaps, leaderboards and revenue rollups written by Hangfire jobs.

| Column | Type | Null | Key / rule |
|---|---|---|---|
| Id | uniqueidentifier | no | PK; default NEWSEQUENTIALID() |
| TenantId | uniqueidentifier | no | FK (TenantId) -> Tenants(Id) |
| Kind | nvarchar(80) | no |  |
| CourseId | uniqueidentifier | yes | FK (TenantId, CourseId) -> Courses(TenantId, Id) tenant-scoped |
| PeriodStartUtc | datetime2(7) | no |  |
| PeriodEndUtc | datetime2(7) | no |  |
| DataJson | nvarchar(max) | no |  |
| ComputedAtUtc | datetime2(7) | no |  |

Indexes: index (Kind, PeriodStartUtc).


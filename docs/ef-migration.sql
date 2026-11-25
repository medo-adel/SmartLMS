IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(256) NOT NULL,
        [NormalizedName] nvarchar(256) NOT NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Tenants] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Slug] nvarchar(100) NOT NULL,
        [Subdomain] nvarchar(100) NULL,
        [LogoUrl] nvarchar(500) NULL,
        [PrimaryColor] nvarchar(9) NULL,
        [SecondaryColor] nvarchar(9) NULL,
        [AccentColor] nvarchar(9) NULL,
        [DefaultLanguage] nvarchar(10) NOT NULL,
        [TimeZoneId] nvarchar(64) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [MaxMobileDevices] int NOT NULL,
        [MaxWebDevices] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Tenants_Rule1] CHECK (([MaxMobileDevices] >= 0 AND [MaxWebDevices] >= 0))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] uniqueidentifier NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [ApiKeys] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [KeyPrefix] nvarchar(12) NOT NULL,
        [KeyHash] nvarchar(128) NOT NULL,
        [ScopesJson] nvarchar(max) NOT NULL,
        [ExpiresAtUtc] datetime2 NULL,
        [LastUsedAtUtc] datetime2 NULL,
        [RevokedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_ApiKeys] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ApiKeys_ScopesJson_Json] CHECK (ISJSON([ScopesJson]) = 1),
        CONSTRAINT [FK_ApiKeys_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NULL,
        [FullName] nvarchar(200) NOT NULL,
        [AvatarUrl] nvarchar(500) NULL,
        [PreferredLanguage] nvarchar(10) NOT NULL,
        [IsActive] bit NOT NULL,
        [LastLoginAtUtc] datetime2 NULL,
        [BiometricLoginEnabled] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        [UserName] nvarchar(256) NOT NULL,
        [NormalizedUserName] nvarchar(256) NOT NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(32) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUsers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Badges] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Code] nvarchar(60) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(500) NULL,
        [IconUrl] nvarchar(500) NULL,
        [RuleJson] nvarchar(max) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Badges] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Badges_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Badges_RuleJson_Json] CHECK (ISJSON([RuleJson]) = 1),
        CONSTRAINT [FK_Badges_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Bundles] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(250) NOT NULL,
        [Description] nvarchar(max) NULL,
        [Price] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Bundles] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Bundles_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Bundles_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Categories] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ParentCategoryId] uniqueidentifier NULL,
        [Name] nvarchar(200) NOT NULL,
        [Slug] nvarchar(200) NOT NULL,
        [SortOrder] int NOT NULL,
        [IconName] nvarchar(60) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Categories] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Categories_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Categories_Categories_TenantId_ParentCategoryId] FOREIGN KEY ([TenantId], [ParentCategoryId]) REFERENCES [Categories] ([TenantId], [Id]),
        CONSTRAINT [FK_Categories_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [SubscriptionPlans] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [Price] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [BillingPeriod] nvarchar(40) NOT NULL,
        [Scope] nvarchar(40) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_SubscriptionPlans] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_SubscriptionPlans_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_SubscriptionPlans_BillingPeriod] CHECK ([BillingPeriod] IN ('Monthly','Quarterly','Yearly')),
        CONSTRAINT [CK_SubscriptionPlans_Scope] CHECK ([Scope] IN ('AllCourses','SelectedCourses')),
        CONSTRAINT [FK_SubscriptionPlans_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [TenantFeatureFlags] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [FeatureKey] nvarchar(100) NOT NULL,
        [IsEnabled] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_TenantFeatureFlags] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TenantFeatureFlags_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [WebhookEndpoints] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Url] nvarchar(1000) NOT NULL,
        [SecretProtected] nvarchar(max) NOT NULL,
        [EventTypesJson] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [ConsecutiveFailures] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_WebhookEndpoints] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_WebhookEndpoints_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_WebhookEndpoints_EventTypesJson_Json] CHECK (ISJSON([EventTypesJson]) = 1),
        CONSTRAINT [FK_WebhookEndpoints_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] uniqueidentifier NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] uniqueidentifier NOT NULL,
        [RoleId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] uniqueidentifier NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NULL,
        [UserId] uniqueidentifier NULL,
        [Action] nvarchar(40) NOT NULL,
        [EntityName] nvarchar(100) NOT NULL,
        [EntityId] nvarchar(64) NULL,
        [OldValuesJson] nvarchar(max) NULL,
        [NewValuesJson] nvarchar(max) NULL,
        [IpAddress] nvarchar(64) NULL,
        [CorrelationId] nvarchar(64) NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AuditLogs_Action] CHECK ([Action] IN ('Create','Update','Delete','Restore','Approve','Reject','Generate','Login','ResetDevice','Other')),
        CONSTRAINT [CK_AuditLogs_NewValuesJson_Json] CHECK (ISJSON([NewValuesJson]) = 1),
        CONSTRAINT [CK_AuditLogs_OldValuesJson_Json] CHECK (ISJSON([OldValuesJson]) = 1),
        CONSTRAINT [FK_AuditLogs_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]),
        CONSTRAINT [FK_AuditLogs_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [OtpCodes] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NULL,
        [UserId] uniqueidentifier NULL,
        [Destination] nvarchar(256) NOT NULL,
        [Purpose] nvarchar(40) NOT NULL,
        [CodeHash] nvarchar(128) NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [ConsumedAtUtc] datetime2 NULL,
        [AttemptCount] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_OtpCodes] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_OtpCodes_Purpose] CHECK ([Purpose] IN ('EmailVerification','PhoneVerification','PasswordReset','Login')),
        CONSTRAINT [FK_OtpCodes_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]),
        CONSTRAINT [FK_OtpCodes_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [UserDevices] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NULL,
        [UserId] uniqueidentifier NOT NULL,
        [DeviceFingerprintHash] nvarchar(128) NOT NULL,
        [DeviceType] nvarchar(40) NOT NULL,
        [Platform] nvarchar(30) NULL,
        [DeviceName] nvarchar(200) NULL,
        [UserAgent] nvarchar(500) NULL,
        [FirstSeenAtUtc] datetime2 NOT NULL,
        [LastSeenAtUtc] datetime2 NOT NULL,
        [LastIp] nvarchar(64) NULL,
        [IsActive] bit NOT NULL,
        [RevokedAtUtc] datetime2 NULL,
        [RevokedBy] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_UserDevices] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_UserDevices_UserId_Id] UNIQUE ([UserId], [Id]),
        CONSTRAINT [CK_UserDevices_DeviceType] CHECK ([DeviceType] IN ('Mobile','Web')),
        CONSTRAINT [FK_UserDevices_AspNetUsers_RevokedBy] FOREIGN KEY ([RevokedBy]) REFERENCES [AspNetUsers] ([Id]),
        CONSTRAINT [FK_UserDevices_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]),
        CONSTRAINT [FK_UserDevices_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [UserTenantMemberships] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(40) NOT NULL,
        [GrantedBy] uniqueidentifier NULL,
        [ExpiresAtUtc] datetime2 NULL,
        [RevokedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_UserTenantMemberships] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_UserTenantMemberships_TenantId_UserId] UNIQUE ([TenantId], [UserId]),
        CONSTRAINT [CK_UserTenantMemberships_Kind] CHECK ([Kind] IN ('Home','SupportAccess')),
        CONSTRAINT [FK_UserTenantMemberships_AspNetUsers_GrantedBy] FOREIGN KEY ([GrantedBy]) REFERENCES [AspNetUsers] ([Id]),
        CONSTRAINT [FK_UserTenantMemberships_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]),
        CONSTRAINT [FK_UserTenantMemberships_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [RefreshTokens] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NULL,
        [UserId] uniqueidentifier NOT NULL,
        [DeviceId] uniqueidentifier NULL,
        [TokenHash] nvarchar(128) NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [RevokedAtUtc] datetime2 NULL,
        [ReplacedByTokenHash] nvarchar(128) NULL,
        [CreatedByIp] nvarchar(64) NULL,
        [RevokedReason] nvarchar(200) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RefreshTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]),
        CONSTRAINT [FK_RefreshTokens_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_RefreshTokens_UserDevices_DeviceId] FOREIGN KEY ([DeviceId]) REFERENCES [UserDevices] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [AffiliateProfiles] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [ReferralCode] nvarchar(40) NOT NULL,
        [CommissionPercent] float NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_AffiliateProfiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AffiliateProfiles_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_AffiliateProfiles_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [BackgroundJobs] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Type] nvarchar(40) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [HangfireJobId] nvarchar(64) NULL,
        [ReferenceType] nvarchar(60) NULL,
        [ReferenceId] uniqueidentifier NULL,
        [RequestedBy] uniqueidentifier NULL,
        [ProgressPercent] int NOT NULL,
        [ErrorMessage] nvarchar(2000) NULL,
        [ResultJson] nvarchar(max) NULL,
        [StartedAtUtc] datetime2 NULL,
        [CompletedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_BackgroundJobs] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_BackgroundJobs_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_BackgroundJobs_ResultJson_Json] CHECK (ISJSON([ResultJson]) = 1),
        CONSTRAINT [CK_BackgroundJobs_Status] CHECK ([Status] IN ('Pending','Processing','Completed','Failed')),
        CONSTRAINT [CK_BackgroundJobs_Type] CHECK ([Type] IN ('AiIngest','AiTranscribe','PlagiarismBatch','VoucherGeneration','WebhookDelivery','CertificatePdf','AnalyticsRollup','Email','Export')),
        CONSTRAINT [FK_BackgroundJobs_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_BackgroundJobs_UserTenantMemberships_TenantId_RequestedBy] FOREIGN KEY ([TenantId], [RequestedBy]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [DeviceResetLogs] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [ResetByUserId] uniqueidentifier NOT NULL,
        [DeviceId] uniqueidentifier NULL,
        [Reason] nvarchar(500) NULL,
        [ResetAtUtc] datetime2 NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_DeviceResetLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DeviceResetLogs_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_DeviceResetLogs_UserDevices_UserId_DeviceId] FOREIGN KEY ([UserId], [DeviceId]) REFERENCES [UserDevices] ([UserId], [Id]),
        CONSTRAINT [FK_DeviceResetLogs_UserTenantMemberships_TenantId_ResetByUserId] FOREIGN KEY ([TenantId], [ResetByUserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId]),
        CONSTRAINT [FK_DeviceResetLogs_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [FileAssets] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OriginalName] nvarchar(260) NOT NULL,
        [StoragePath] nvarchar(500) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [Sha256] nvarchar(64) NULL,
        [UploadedBy] uniqueidentifier NULL,
        [Purpose] nvarchar(40) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_FileAssets] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_FileAssets_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_FileAssets_Purpose] CHECK ([Purpose] IN ('CourseMedia','LessonResource','Receipt','Submission','Certificate','Invoice','Avatar','Export','Other')),
        CONSTRAINT [FK_FileAssets_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_FileAssets_UserTenantMemberships_TenantId_UploadedBy] FOREIGN KEY ([TenantId], [UploadedBy]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [InstructorPayouts] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [InstructorId] uniqueidentifier NOT NULL,
        [PeriodStartUtc] datetime2 NOT NULL,
        [PeriodEndUtc] datetime2 NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [PaidAtUtc] datetime2 NULL,
        [PaymentReference] nvarchar(200) NULL,
        [ApprovedBy] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_InstructorPayouts] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_InstructorPayouts_Status] CHECK ([Status] IN ('Calculated','Approved','Paid','Cancelled')),
        CONSTRAINT [FK_InstructorPayouts_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_InstructorPayouts_UserTenantMemberships_TenantId_ApprovedBy] FOREIGN KEY ([TenantId], [ApprovedBy]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId]),
        CONSTRAINT [FK_InstructorPayouts_UserTenantMemberships_TenantId_InstructorId] FOREIGN KEY ([TenantId], [InstructorId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Type] nvarchar(60) NOT NULL,
        [Title] nvarchar(250) NOT NULL,
        [Body] nvarchar(2000) NULL,
        [DataJson] nvarchar(max) NULL,
        [IsRead] bit NOT NULL,
        [ReadAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Notifications_DataJson_Json] CHECK (ISJSON([DataJson]) = 1),
        CONSTRAINT [FK_Notifications_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_Notifications_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [ParentAlerts] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ParentUserId] uniqueidentifier NOT NULL,
        [StudentUserId] uniqueidentifier NOT NULL,
        [AlertType] nvarchar(40) NOT NULL,
        [Message] nvarchar(1000) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ReadAtUtc] datetime2 NULL,
        CONSTRAINT [PK_ParentAlerts] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ParentAlerts_AlertType] CHECK ([AlertType] IN ('LowQuizScore','LowAttendance','Inactivity','OverdueAssignment','HighDropoutRisk')),
        CONSTRAINT [FK_ParentAlerts_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_ParentAlerts_UserTenantMemberships_TenantId_ParentUserId] FOREIGN KEY ([TenantId], [ParentUserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId]),
        CONSTRAINT [FK_ParentAlerts_UserTenantMemberships_TenantId_StudentUserId] FOREIGN KEY ([TenantId], [StudentUserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [ParentStudentLinks] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ParentUserId] uniqueidentifier NOT NULL,
        [StudentUserId] uniqueidentifier NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [LinkCodeHash] nvarchar(128) NULL,
        [ApprovedBy] uniqueidentifier NULL,
        [ApprovedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_ParentStudentLinks] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ParentStudentLinks_Status] CHECK ([Status] IN ('Pending','Active','Revoked')),
        CONSTRAINT [FK_ParentStudentLinks_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_ParentStudentLinks_UserTenantMemberships_TenantId_ApprovedBy] FOREIGN KEY ([TenantId], [ApprovedBy]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId]),
        CONSTRAINT [FK_ParentStudentLinks_UserTenantMemberships_TenantId_ParentUserId] FOREIGN KEY ([TenantId], [ParentUserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId]),
        CONSTRAINT [FK_ParentStudentLinks_UserTenantMemberships_TenantId_StudentUserId] FOREIGN KEY ([TenantId], [StudentUserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [PushTokens] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [DeviceId] uniqueidentifier NULL,
        [FcmToken] nvarchar(500) NOT NULL,
        [Platform] nvarchar(40) NOT NULL,
        [LastSeenAtUtc] datetime2 NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_PushTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PushTokens_Platform] CHECK ([Platform] IN ('Android','Ios','Web')),
        CONSTRAINT [FK_PushTokens_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_PushTokens_UserDevices_UserId_DeviceId] FOREIGN KEY ([UserId], [DeviceId]) REFERENCES [UserDevices] ([UserId], [Id]),
        CONSTRAINT [FK_PushTokens_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Referrals] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [AffiliateUserId] uniqueidentifier NOT NULL,
        [ReferredUserId] uniqueidentifier NOT NULL,
        [ReferralCodeUsed] nvarchar(40) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Referrals] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Referrals_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_Referrals_UserTenantMemberships_TenantId_AffiliateUserId] FOREIGN KEY ([TenantId], [AffiliateUserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId]),
        CONSTRAINT [FK_Referrals_UserTenantMemberships_TenantId_ReferredUserId] FOREIGN KEY ([TenantId], [ReferredUserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [UserBadges] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [BadgeId] uniqueidentifier NOT NULL,
        [AwardedAtUtc] datetime2 NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_UserBadges] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserBadges_Badges_TenantId_BadgeId] FOREIGN KEY ([TenantId], [BadgeId]) REFERENCES [Badges] ([TenantId], [Id]),
        CONSTRAINT [FK_UserBadges_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_UserBadges_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [UserStreaks] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [CurrentStreakDays] int NOT NULL,
        [LongestStreakDays] int NOT NULL,
        [LastActivityDate] date NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_UserStreaks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserStreaks_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_UserStreaks_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [UserSubscriptions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [PlanId] uniqueidentifier NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [StartedAtUtc] datetime2 NOT NULL,
        [CurrentPeriodEndUtc] datetime2 NOT NULL,
        [CancelledAtUtc] datetime2 NULL,
        [GatewayReference] nvarchar(200) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_UserSubscriptions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_UserSubscriptions_Status] CHECK ([Status] IN ('Active','PastDue','Cancelled','Expired')),
        CONSTRAINT [FK_UserSubscriptions_SubscriptionPlans_TenantId_PlanId] FOREIGN KEY ([TenantId], [PlanId]) REFERENCES [SubscriptionPlans] ([TenantId], [Id]),
        CONSTRAINT [FK_UserSubscriptions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_UserSubscriptions_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Wallets] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Balance] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Wallets] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Wallets_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Wallets_Rule1] CHECK (([Balance] >= 0)),
        CONSTRAINT [FK_Wallets_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_Wallets_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [XpTransactions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Points] int NOT NULL,
        [Reason] nvarchar(100) NOT NULL,
        [SourceType] nvarchar(60) NULL,
        [SourceId] uniqueidentifier NULL,
        [AwardedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_XpTransactions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_XpTransactions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_XpTransactions_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [WebhookDeliveries] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [WebhookEndpointId] uniqueidentifier NOT NULL,
        [EventType] nvarchar(100) NOT NULL,
        [PayloadJson] nvarchar(max) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [AttemptCount] int NOT NULL,
        [NextAttemptAtUtc] datetime2 NULL,
        [LastHttpStatus] int NULL,
        [LastError] nvarchar(1000) NULL,
        [DeliveredAtUtc] datetime2 NULL,
        [BackgroundJobId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_WebhookDeliveries] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_WebhookDeliveries_PayloadJson_Json] CHECK (ISJSON([PayloadJson]) = 1),
        CONSTRAINT [CK_WebhookDeliveries_Status] CHECK ([Status] IN ('Pending','Delivered','Failed','Abandoned')),
        CONSTRAINT [FK_WebhookDeliveries_BackgroundJobs_TenantId_BackgroundJobId] FOREIGN KEY ([TenantId], [BackgroundJobId]) REFERENCES [BackgroundJobs] ([TenantId], [Id]),
        CONSTRAINT [FK_WebhookDeliveries_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_WebhookDeliveries_WebhookEndpoints_TenantId_WebhookEndpointId] FOREIGN KEY ([TenantId], [WebhookEndpointId]) REFERENCES [WebhookEndpoints] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Courses] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CategoryId] uniqueidentifier NULL,
        [OwnerInstructorId] uniqueidentifier NOT NULL,
        [Title] nvarchar(250) NOT NULL,
        [Slug] nvarchar(250) NOT NULL,
        [ShortDescription] nvarchar(500) NULL,
        [Description] nvarchar(max) NULL,
        [ThumbnailFileId] uniqueidentifier NULL,
        [PromoVideoUrl] nvarchar(500) NULL,
        [Level] nvarchar(40) NOT NULL,
        [Language] nvarchar(10) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [PricingModel] nvarchar(40) NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [DripMode] nvarchar(40) NOT NULL,
        [AccessDays] int NULL,
        [EstimatedHours] float NULL,
        [PublishedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Courses] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Courses_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Courses_DripMode] CHECK ([DripMode] IN ('None','ByDate','DaysAfterEnrollment')),
        CONSTRAINT [CK_Courses_Level] CHECK ([Level] IN ('Beginner','Intermediate','Advanced','AllLevels')),
        CONSTRAINT [CK_Courses_PricingModel] CHECK ([PricingModel] IN ('Free','OneTime','PayPerLesson','SubscriptionOnly')),
        CONSTRAINT [CK_Courses_Status] CHECK ([Status] IN ('Draft','PendingApproval','Published','Rejected','Archived')),
        CONSTRAINT [FK_Courses_Categories_TenantId_CategoryId] FOREIGN KEY ([TenantId], [CategoryId]) REFERENCES [Categories] ([TenantId], [Id]),
        CONSTRAINT [FK_Courses_FileAssets_TenantId_ThumbnailFileId] FOREIGN KEY ([TenantId], [ThumbnailFileId]) REFERENCES [FileAssets] ([TenantId], [Id]),
        CONSTRAINT [FK_Courses_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_Courses_UserTenantMemberships_TenantId_OwnerInstructorId] FOREIGN KEY ([TenantId], [OwnerInstructorId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [LearningPaths] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Title] nvarchar(250) NOT NULL,
        [Slug] nvarchar(250) NOT NULL,
        [Description] nvarchar(max) NULL,
        [ThumbnailFileId] uniqueidentifier NULL,
        [Status] nvarchar(40) NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [IssuesCertificate] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_LearningPaths] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_LearningPaths_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_LearningPaths_Status] CHECK ([Status] IN ('Draft','Published','Archived')),
        CONSTRAINT [FK_LearningPaths_FileAssets_TenantId_ThumbnailFileId] FOREIGN KEY ([TenantId], [ThumbnailFileId]) REFERENCES [FileAssets] ([TenantId], [Id]),
        CONSTRAINT [FK_LearningPaths_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [WalletTransactions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [WalletId] uniqueidentifier NOT NULL,
        [Type] nvarchar(40) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [BalanceAfter] decimal(18,2) NOT NULL,
        [ReferenceType] nvarchar(60) NULL,
        [ReferenceId] uniqueidentifier NULL,
        [Description] nvarchar(300) NULL,
        [IdempotencyKey] nvarchar(100) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_WalletTransactions] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_WalletTransactions_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_WalletTransactions_Type] CHECK ([Type] IN ('TopUpGateway','TopUpVoucher','TopUpManual','Purchase','Refund','AffiliateCommission','Adjustment')),
        CONSTRAINT [FK_WalletTransactions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_WalletTransactions_UserTenantMemberships_TenantId_CreatedBy] FOREIGN KEY ([TenantId], [CreatedBy]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId]),
        CONSTRAINT [FK_WalletTransactions_Wallets_TenantId_WalletId] FOREIGN KEY ([TenantId], [WalletId]) REFERENCES [Wallets] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [AiUsageLogs] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NULL,
        [Endpoint] nvarchar(80) NOT NULL,
        [CourseId] uniqueidentifier NULL,
        [Provider] nvarchar(30) NULL,
        [LatencyMs] int NULL,
        [StatusCode] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AiUsageLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AiUsageLogs_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_AiUsageLogs_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_AiUsageLogs_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [AnalyticsSnapshots] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(80) NOT NULL,
        [CourseId] uniqueidentifier NULL,
        [PeriodStartUtc] datetime2 NOT NULL,
        [PeriodEndUtc] datetime2 NOT NULL,
        [DataJson] nvarchar(max) NOT NULL,
        [ComputedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AnalyticsSnapshots] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AnalyticsSnapshots_DataJson_Json] CHECK (ISJSON([DataJson]) = 1),
        CONSTRAINT [FK_AnalyticsSnapshots_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_AnalyticsSnapshots_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Announcements] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NULL,
        [AuthorId] uniqueidentifier NOT NULL,
        [Title] nvarchar(250) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [PublishedAtUtc] datetime2 NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Announcements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Announcements_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_Announcements_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_Announcements_UserTenantMemberships_TenantId_AuthorId] FOREIGN KEY ([TenantId], [AuthorId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [BundleCourses] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [BundleId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_BundleCourses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BundleCourses_Bundles_TenantId_BundleId] FOREIGN KEY ([TenantId], [BundleId]) REFERENCES [Bundles] ([TenantId], [Id]),
        CONSTRAINT [FK_BundleCourses_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_BundleCourses_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [ChatRooms] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NULL,
        [RoomType] nvarchar(40) NOT NULL,
        [Name] nvarchar(200) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_ChatRooms] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_ChatRooms_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_ChatRooms_RoomType] CHECK ([RoomType] IN ('Course','Direct','Group')),
        CONSTRAINT [FK_ChatRooms_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_ChatRooms_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Coupons] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [DiscountType] nvarchar(40) NOT NULL,
        [Value] decimal(18,2) NOT NULL,
        [MaxRedemptions] int NULL,
        [MaxPerUser] int NOT NULL,
        [MinOrderAmount] decimal(18,2) NULL,
        [ExpiresAtUtc] datetime2 NULL,
        [CourseId] uniqueidentifier NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Coupons] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Coupons_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Coupons_DiscountType] CHECK ([DiscountType] IN ('Percentage','Fixed')),
        CONSTRAINT [CK_Coupons_Rule1] CHECK (([Value] > 0)),
        CONSTRAINT [FK_Coupons_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_Coupons_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [CourseApprovals] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [SubmittedBy] uniqueidentifier NOT NULL,
        [SubmittedAtUtc] datetime2 NOT NULL,
        [ReviewedBy] uniqueidentifier NULL,
        [ReviewedAtUtc] datetime2 NULL,
        [Decision] nvarchar(40) NOT NULL,
        [Comment] nvarchar(1000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_CourseApprovals] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CourseApprovals_Decision] CHECK ([Decision] IN ('Pending','Approved','Rejected')),
        CONSTRAINT [FK_CourseApprovals_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_CourseApprovals_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_CourseApprovals_UserTenantMemberships_TenantId_ReviewedBy] FOREIGN KEY ([TenantId], [ReviewedBy]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId]),
        CONSTRAINT [FK_CourseApprovals_UserTenantMemberships_TenantId_SubmittedBy] FOREIGN KEY ([TenantId], [SubmittedBy]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [CoursePrerequisites] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [PrerequisiteCourseId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_CoursePrerequisites] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CoursePrerequisites_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_CoursePrerequisites_Courses_TenantId_PrerequisiteCourseId] FOREIGN KEY ([TenantId], [PrerequisiteCourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_CoursePrerequisites_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [CourseStaff] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [StaffRole] nvarchar(40) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_CourseStaff] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CourseStaff_StaffRole] CHECK ([StaffRole] IN ('CoInstructor','TeachingAssistant')),
        CONSTRAINT [FK_CourseStaff_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_CourseStaff_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_CourseStaff_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [DropoutRiskScores] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [Score] float NOT NULL,
        [Level] nvarchar(40) NOT NULL,
        [FeaturesJson] nvarchar(max) NOT NULL,
        [ComputedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_DropoutRiskScores] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DropoutRiskScores_FeaturesJson_Json] CHECK (ISJSON([FeaturesJson]) = 1),
        CONSTRAINT [CK_DropoutRiskScores_Level] CHECK ([Level] IN ('Low','Medium','High')),
        CONSTRAINT [FK_DropoutRiskScores_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_DropoutRiskScores_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_DropoutRiskScores_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [InstructorRevenueShares] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [InstructorId] uniqueidentifier NOT NULL,
        [SharePercent] float NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_InstructorRevenueShares] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InstructorRevenueShares_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_InstructorRevenueShares_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_InstructorRevenueShares_UserTenantMemberships_TenantId_InstructorId] FOREIGN KEY ([TenantId], [InstructorId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [LiveSessions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [HostId] uniqueidentifier NOT NULL,
        [Title] nvarchar(250) NOT NULL,
        [Provider] nvarchar(40) NOT NULL,
        [JoinUrl] nvarchar(1000) NOT NULL,
        [StartsAtUtc] datetime2 NOT NULL,
        [EndsAtUtc] datetime2 NULL,
        [Status] nvarchar(40) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_LiveSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_LiveSessions_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_LiveSessions_Provider] CHECK ([Provider] IN ('Zoom','GoogleMeet','Jitsi','Other')),
        CONSTRAINT [CK_LiveSessions_Status] CHECK ([Status] IN ('Scheduled','Live','Ended','Cancelled')),
        CONSTRAINT [FK_LiveSessions_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_LiveSessions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_LiveSessions_UserTenantMemberships_TenantId_HostId] FOREIGN KEY ([TenantId], [HostId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [QuestionBanks] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NULL,
        [Name] nvarchar(250) NOT NULL,
        [Topic] nvarchar(200) NULL,
        [Description] nvarchar(1000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_QuestionBanks] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_QuestionBanks_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_QuestionBanks_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_QuestionBanks_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Sections] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [Title] nvarchar(250) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [SortOrder] int NOT NULL,
        [DripUnlockAtUtc] datetime2 NULL,
        [DripDaysAfterEnrollment] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Sections] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Sections_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Sections_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_Sections_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [SubscriptionPlanCourses] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [PlanId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_SubscriptionPlanCourses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubscriptionPlanCourses_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_SubscriptionPlanCourses_SubscriptionPlans_TenantId_PlanId] FOREIGN KEY ([TenantId], [PlanId]) REFERENCES [SubscriptionPlans] ([TenantId], [Id]),
        CONSTRAINT [FK_SubscriptionPlanCourses_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [VoucherBatches] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Prefix] nvarchar(12) NULL,
        [Quantity] int NOT NULL,
        [VoucherType] nvarchar(40) NOT NULL,
        [CourseId] uniqueidentifier NULL,
        [WalletAmount] decimal(18,2) NULL,
        [ExpiresAtUtc] datetime2 NULL,
        [BackgroundJobId] uniqueidentifier NULL,
        [ExportFileId] uniqueidentifier NULL,
        [Status] nvarchar(40) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_VoucherBatches] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_VoucherBatches_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_VoucherBatches_Status] CHECK ([Status] IN ('Pending','Generating','Ready','Failed')),
        CONSTRAINT [CK_VoucherBatches_VoucherType] CHECK ([VoucherType] IN ('CourseAccess','WalletCredit')),
        CONSTRAINT [FK_VoucherBatches_BackgroundJobs_TenantId_BackgroundJobId] FOREIGN KEY ([TenantId], [BackgroundJobId]) REFERENCES [BackgroundJobs] ([TenantId], [Id]),
        CONSTRAINT [FK_VoucherBatches_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_VoucherBatches_FileAssets_TenantId_ExportFileId] FOREIGN KEY ([TenantId], [ExportFileId]) REFERENCES [FileAssets] ([TenantId], [Id]),
        CONSTRAINT [FK_VoucherBatches_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Certificates] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NULL,
        [LearningPathId] uniqueidentifier NULL,
        [VerificationCode] nvarchar(32) NOT NULL,
        [IssuedAtUtc] datetime2 NOT NULL,
        [PdfFileId] uniqueidentifier NULL,
        [RevokedAtUtc] datetime2 NULL,
        [RevokedReason] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Certificates] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Certificates_Rule1] CHECK ((([CourseId] IS NOT NULL AND [LearningPathId] IS NULL) OR ([CourseId] IS NULL AND [LearningPathId] IS NOT NULL))),
        CONSTRAINT [FK_Certificates_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_Certificates_FileAssets_TenantId_PdfFileId] FOREIGN KEY ([TenantId], [PdfFileId]) REFERENCES [FileAssets] ([TenantId], [Id]),
        CONSTRAINT [FK_Certificates_LearningPaths_TenantId_LearningPathId] FOREIGN KEY ([TenantId], [LearningPathId]) REFERENCES [LearningPaths] ([TenantId], [Id]),
        CONSTRAINT [FK_Certificates_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_Certificates_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [LearningPathCourses] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [LearningPathId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [SortOrder] int NOT NULL,
        [IsMandatory] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_LearningPathCourses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LearningPathCourses_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_LearningPathCourses_LearningPaths_TenantId_LearningPathId] FOREIGN KEY ([TenantId], [LearningPathId]) REFERENCES [LearningPaths] ([TenantId], [Id]),
        CONSTRAINT [FK_LearningPathCourses_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [LearningPathEnrollments] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [LearningPathId] uniqueidentifier NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [EnrolledAtUtc] datetime2 NOT NULL,
        [CompletedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_LearningPathEnrollments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_LearningPathEnrollments_Status] CHECK ([Status] IN ('Active','Completed','Revoked')),
        CONSTRAINT [FK_LearningPathEnrollments_LearningPaths_TenantId_LearningPathId] FOREIGN KEY ([TenantId], [LearningPathId]) REFERENCES [LearningPaths] ([TenantId], [Id]),
        CONSTRAINT [FK_LearningPathEnrollments_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_LearningPathEnrollments_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [ChatMessages] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ChatRoomId] uniqueidentifier NOT NULL,
        [SenderId] uniqueidentifier NOT NULL,
        [Body] nvarchar(4000) NOT NULL,
        [SentAtUtc] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_ChatMessages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ChatMessages_ChatRooms_TenantId_ChatRoomId] FOREIGN KEY ([TenantId], [ChatRoomId]) REFERENCES [ChatRooms] ([TenantId], [Id]),
        CONSTRAINT [FK_ChatMessages_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_ChatMessages_UserTenantMemberships_TenantId_SenderId] FOREIGN KEY ([TenantId], [SenderId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [ChatRoomMembers] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ChatRoomId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [JoinedAtUtc] datetime2 NOT NULL,
        [LastReadAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_ChatRoomMembers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ChatRoomMembers_ChatRooms_TenantId_ChatRoomId] FOREIGN KEY ([TenantId], [ChatRoomId]) REFERENCES [ChatRooms] ([TenantId], [Id]),
        CONSTRAINT [FK_ChatRoomMembers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_ChatRoomMembers_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Orders] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [Subtotal] decimal(18,2) NOT NULL,
        [DiscountTotal] decimal(18,2) NOT NULL,
        [Total] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [CouponId] uniqueidentifier NULL,
        [PaymentMethod] nvarchar(40) NOT NULL,
        [PaidAtUtc] datetime2 NULL,
        [ReferralCode] nvarchar(50) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Orders] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Orders_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Orders_PaymentMethod] CHECK ([PaymentMethod] IN ('Gateway','Wallet','Voucher','Manual','Free')),
        CONSTRAINT [CK_Orders_Status] CHECK ([Status] IN ('Pending','AwaitingManualReview','Paid','Failed','Cancelled','Refunded')),
        CONSTRAINT [FK_Orders_Coupons_TenantId_CouponId] FOREIGN KEY ([TenantId], [CouponId]) REFERENCES [Coupons] ([TenantId], [Id]),
        CONSTRAINT [FK_Orders_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_Orders_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [LiveSessionAttendance] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [LiveSessionId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [JoinedAtUtc] datetime2 NOT NULL,
        [LeftAtUtc] datetime2 NULL,
        [DurationSeconds] int NULL,
        [Source] nvarchar(40) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_LiveSessionAttendance] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_LiveSessionAttendance_Source] CHECK ([Source] IN ('JoinLink','Provider','Manual')),
        CONSTRAINT [FK_LiveSessionAttendance_LiveSessions_TenantId_LiveSessionId] FOREIGN KEY ([TenantId], [LiveSessionId]) REFERENCES [LiveSessions] ([TenantId], [Id]),
        CONSTRAINT [FK_LiveSessionAttendance_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_LiveSessionAttendance_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Questions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [QuestionBankId] uniqueidentifier NOT NULL,
        [QuestionType] nvarchar(40) NOT NULL,
        [Text] nvarchar(max) NOT NULL,
        [Difficulty] nvarchar(40) NOT NULL,
        [Topic] nvarchar(200) NULL,
        [DefaultPoints] float NOT NULL,
        [Explanation] nvarchar(max) NULL,
        [DataJson] nvarchar(max) NULL,
        [Source] nvarchar(40) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Questions] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Questions_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Questions_DataJson_Json] CHECK (ISJSON([DataJson]) = 1),
        CONSTRAINT [CK_Questions_Difficulty] CHECK ([Difficulty] IN ('Easy','Medium','Hard')),
        CONSTRAINT [CK_Questions_QuestionType] CHECK ([QuestionType] IN ('McqSingle','McqMulti','TrueFalse','FillBlank','Matching','ShortAnswer','Essay')),
        CONSTRAINT [CK_Questions_Source] CHECK ([Source] IN ('Manual','Ai')),
        CONSTRAINT [FK_Questions_QuestionBanks_TenantId_QuestionBankId] FOREIGN KEY ([TenantId], [QuestionBankId]) REFERENCES [QuestionBanks] ([TenantId], [Id]),
        CONSTRAINT [FK_Questions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Exams] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [SectionId] uniqueidentifier NULL,
        [Title] nvarchar(250) NOT NULL,
        [Kind] nvarchar(40) NOT NULL,
        [Instructions] nvarchar(max) NULL,
        [DurationMinutes] int NULL,
        [MaxAttempts] int NOT NULL,
        [PassingScorePercent] float NOT NULL,
        [ShuffleQuestions] bit NOT NULL,
        [ShuffleOptions] bit NOT NULL,
        [AvailableFromUtc] datetime2 NULL,
        [AvailableUntilUtc] datetime2 NULL,
        [TabSwitchDetection] bit NOT NULL,
        [MaxTabSwitches] int NULL,
        [RequirePrivacyScreen] bit NOT NULL,
        [IsPublished] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Exams] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Exams_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Exams_Kind] CHECK ([Kind] IN ('Quiz','Exam')),
        CONSTRAINT [FK_Exams_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_Exams_Sections_TenantId_SectionId] FOREIGN KEY ([TenantId], [SectionId]) REFERENCES [Sections] ([TenantId], [Id]),
        CONSTRAINT [FK_Exams_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Lessons] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [SectionId] uniqueidentifier NOT NULL,
        [Title] nvarchar(250) NOT NULL,
        [LessonType] nvarchar(40) NOT NULL,
        [ContentMarkdown] nvarchar(max) NULL,
        [ContentFileId] uniqueidentifier NULL,
        [ExternalUrl] nvarchar(1000) NULL,
        [DurationSeconds] int NULL,
        [SortOrder] int NOT NULL,
        [IsFreePreview] bit NOT NULL,
        [PayPerLessonPrice] decimal(18,2) NULL,
        [PrerequisiteLessonId] uniqueidentifier NULL,
        [DripUnlockAtUtc] datetime2 NULL,
        [DripDaysAfterEnrollment] int NULL,
        [IsPublished] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Lessons] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Lessons_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Lessons_LessonType] CHECK ([LessonType] IN ('Video','Article','Pdf','Audio','ExternalLink','InteractiveSlides')),
        CONSTRAINT [FK_Lessons_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_Lessons_FileAssets_TenantId_ContentFileId] FOREIGN KEY ([TenantId], [ContentFileId]) REFERENCES [FileAssets] ([TenantId], [Id]),
        CONSTRAINT [FK_Lessons_Lessons_TenantId_PrerequisiteLessonId] FOREIGN KEY ([TenantId], [PrerequisiteLessonId]) REFERENCES [Lessons] ([TenantId], [Id]),
        CONSTRAINT [FK_Lessons_Sections_TenantId_SectionId] FOREIGN KEY ([TenantId], [SectionId]) REFERENCES [Sections] ([TenantId], [Id]),
        CONSTRAINT [FK_Lessons_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Vouchers] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [VoucherBatchId] uniqueidentifier NOT NULL,
        [CodeHash] nvarchar(64) NOT NULL,
        [CodeLast4] nvarchar(4) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [RedeemedBy] uniqueidentifier NULL,
        [RedeemedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Vouchers] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Vouchers_Status] CHECK ([Status] IN ('Unused','Redeemed','Void')),
        CONSTRAINT [FK_Vouchers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_Vouchers_UserTenantMemberships_TenantId_RedeemedBy] FOREIGN KEY ([TenantId], [RedeemedBy]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId]),
        CONSTRAINT [FK_Vouchers_VoucherBatches_TenantId_VoucherBatchId] FOREIGN KEY ([TenantId], [VoucherBatchId]) REFERENCES [VoucherBatches] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [AffiliateCommissions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [AffiliateUserId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [WalletTransactionId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_AffiliateCommissions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AffiliateCommissions_Status] CHECK ([Status] IN ('Pending','Credited','Reversed')),
        CONSTRAINT [FK_AffiliateCommissions_Orders_TenantId_OrderId] FOREIGN KEY ([TenantId], [OrderId]) REFERENCES [Orders] ([TenantId], [Id]),
        CONSTRAINT [FK_AffiliateCommissions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_AffiliateCommissions_UserTenantMemberships_TenantId_AffiliateUserId] FOREIGN KEY ([TenantId], [AffiliateUserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId]),
        CONSTRAINT [FK_AffiliateCommissions_WalletTransactions_TenantId_WalletTransactionId] FOREIGN KEY ([TenantId], [WalletTransactionId]) REFERENCES [WalletTransactions] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [CouponRedemptions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CouponId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [RedeemedAtUtc] datetime2 NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_CouponRedemptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CouponRedemptions_Coupons_TenantId_CouponId] FOREIGN KEY ([TenantId], [CouponId]) REFERENCES [Coupons] ([TenantId], [Id]),
        CONSTRAINT [FK_CouponRedemptions_Orders_TenantId_OrderId] FOREIGN KEY ([TenantId], [OrderId]) REFERENCES [Orders] ([TenantId], [Id]),
        CONSTRAINT [FK_CouponRedemptions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_CouponRedemptions_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Invoices] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [InvoiceNumber] nvarchar(40) NOT NULL,
        [IssuedAtUtc] datetime2 NOT NULL,
        [PdfFileId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Invoices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Invoices_FileAssets_TenantId_PdfFileId] FOREIGN KEY ([TenantId], [PdfFileId]) REFERENCES [FileAssets] ([TenantId], [Id]),
        CONSTRAINT [FK_Invoices_Orders_TenantId_OrderId] FOREIGN KEY ([TenantId], [OrderId]) REFERENCES [Orders] ([TenantId], [Id]),
        CONSTRAINT [FK_Invoices_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [ManualPaymentSubmissions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NULL,
        [Purpose] nvarchar(40) NOT NULL,
        [Method] nvarchar(40) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [SenderReference] nvarchar(200) NULL,
        [SenderPhoneOrAccount] nvarchar(100) NULL,
        [TransferredAtUtc] datetime2 NULL,
        [ReceiptFileId] uniqueidentifier NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [ReviewedBy] uniqueidentifier NULL,
        [ReviewedAtUtc] datetime2 NULL,
        [RejectionReason] nvarchar(500) NULL,
        [WalletTransactionId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_ManualPaymentSubmissions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ManualPaymentSubmissions_Method] CHECK ([Method] IN ('VodafoneCash','InstaPay','BankTransfer')),
        CONSTRAINT [CK_ManualPaymentSubmissions_Purpose] CHECK ([Purpose] IN ('Order','WalletTopUp')),
        CONSTRAINT [CK_ManualPaymentSubmissions_Status] CHECK ([Status] IN ('Pending','Approved','Rejected')),
        CONSTRAINT [FK_ManualPaymentSubmissions_FileAssets_TenantId_ReceiptFileId] FOREIGN KEY ([TenantId], [ReceiptFileId]) REFERENCES [FileAssets] ([TenantId], [Id]),
        CONSTRAINT [FK_ManualPaymentSubmissions_Orders_TenantId_OrderId] FOREIGN KEY ([TenantId], [OrderId]) REFERENCES [Orders] ([TenantId], [Id]),
        CONSTRAINT [FK_ManualPaymentSubmissions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_ManualPaymentSubmissions_UserTenantMemberships_TenantId_ReviewedBy] FOREIGN KEY ([TenantId], [ReviewedBy]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId]),
        CONSTRAINT [FK_ManualPaymentSubmissions_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId]),
        CONSTRAINT [FK_ManualPaymentSubmissions_WalletTransactions_TenantId_WalletTransactionId] FOREIGN KEY ([TenantId], [WalletTransactionId]) REFERENCES [WalletTransactions] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Payments] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [Method] nvarchar(40) NOT NULL,
        [GatewayReference] nvarchar(200) NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [FailureReason] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ConfirmedAtUtc] datetime2 NULL,
        [IdempotencyKey] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Payments_Method] CHECK ([Method] IN ('Paymob','Fawry','Stripe','Wallet','Manual','Voucher')),
        CONSTRAINT [CK_Payments_Status] CHECK ([Status] IN ('Pending','Succeeded','Failed','Refunded')),
        CONSTRAINT [FK_Payments_Orders_TenantId_OrderId] FOREIGN KEY ([TenantId], [OrderId]) REFERENCES [Orders] ([TenantId], [Id]),
        CONSTRAINT [FK_Payments_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [QuestionOptions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [QuestionId] uniqueidentifier NOT NULL,
        [Text] nvarchar(max) NOT NULL,
        [IsCorrect] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [MatchKey] nvarchar(100) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_QuestionOptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_QuestionOptions_Questions_TenantId_QuestionId] FOREIGN KEY ([TenantId], [QuestionId]) REFERENCES [Questions] ([TenantId], [Id]),
        CONSTRAINT [FK_QuestionOptions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [ExamAttempts] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ExamId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [AttemptNumber] int NOT NULL,
        [StartedAtUtc] datetime2 NOT NULL,
        [ExpiresAtUtc] datetime2 NULL,
        [SubmittedAtUtc] datetime2 NULL,
        [Status] nvarchar(40) NOT NULL,
        [Score] float NULL,
        [MaxScore] float NULL,
        [Passed] bit NULL,
        [TabSwitchCount] int NOT NULL,
        [DeviceId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_ExamAttempts] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_ExamAttempts_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_ExamAttempts_Status] CHECK ([Status] IN ('InProgress','Submitted','Graded','Expired','Voided')),
        CONSTRAINT [FK_ExamAttempts_Exams_TenantId_ExamId] FOREIGN KEY ([TenantId], [ExamId]) REFERENCES [Exams] ([TenantId], [Id]),
        CONSTRAINT [FK_ExamAttempts_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_ExamAttempts_UserDevices_UserId_DeviceId] FOREIGN KEY ([UserId], [DeviceId]) REFERENCES [UserDevices] ([UserId], [Id]),
        CONSTRAINT [FK_ExamAttempts_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [ExamQuestionRules] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ExamId] uniqueidentifier NOT NULL,
        [QuestionId] uniqueidentifier NULL,
        [QuestionBankId] uniqueidentifier NULL,
        [PickCount] int NULL,
        [Difficulty] nvarchar(40) NULL,
        [Topic] nvarchar(200) NULL,
        [Points] float NULL,
        [SortOrder] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_ExamQuestionRules] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ExamQuestionRules_Difficulty] CHECK ([Difficulty] IN ('Easy','Medium','Hard')),
        CONSTRAINT [CK_ExamQuestionRules_Rule1] CHECK ((([QuestionId] IS NOT NULL AND [QuestionBankId] IS NULL) OR ([QuestionId] IS NULL AND [QuestionBankId] IS NOT NULL AND [PickCount] > 0))),
        CONSTRAINT [FK_ExamQuestionRules_Exams_TenantId_ExamId] FOREIGN KEY ([TenantId], [ExamId]) REFERENCES [Exams] ([TenantId], [Id]),
        CONSTRAINT [FK_ExamQuestionRules_QuestionBanks_TenantId_QuestionBankId] FOREIGN KEY ([TenantId], [QuestionBankId]) REFERENCES [QuestionBanks] ([TenantId], [Id]),
        CONSTRAINT [FK_ExamQuestionRules_Questions_TenantId_QuestionId] FOREIGN KEY ([TenantId], [QuestionId]) REFERENCES [Questions] ([TenantId], [Id]),
        CONSTRAINT [FK_ExamQuestionRules_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Assignments] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [LessonId] uniqueidentifier NULL,
        [Title] nvarchar(250) NOT NULL,
        [Instructions] nvarchar(max) NULL,
        [DueAtUtc] datetime2 NULL,
        [MaxPoints] float NOT NULL,
        [AllowResubmission] bit NOT NULL,
        [MaxResubmissions] int NULL,
        [IsGroup] bit NOT NULL,
        [MaxGroupSize] int NULL,
        [PeerReviewEnabled] bit NOT NULL,
        [PeerReviewsPerSubmission] int NULL,
        [PeerReviewDueAtUtc] datetime2 NULL,
        [PlagiarismCheckEnabled] bit NOT NULL,
        [AiProvisionalGradingEnabled] bit NOT NULL,
        [IsPublished] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Assignments] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Assignments_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Assignments_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_Assignments_Lessons_TenantId_LessonId] FOREIGN KEY ([TenantId], [LessonId]) REFERENCES [Lessons] ([TenantId], [Id]),
        CONSTRAINT [FK_Assignments_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [CartItems] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [ItemType] nvarchar(40) NOT NULL,
        [CourseId] uniqueidentifier NULL,
        [LessonId] uniqueidentifier NULL,
        [BundleId] uniqueidentifier NULL,
        [PlanId] uniqueidentifier NULL,
        [LearningPathId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_CartItems] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CartItems_ItemType] CHECK ([ItemType] IN ('Course','Lesson','Bundle','Plan','Path')),
        CONSTRAINT [CK_CartItems_Rule1] CHECK (((CASE WHEN [CourseId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [LessonId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BundleId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [PlanId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [LearningPathId] IS NULL THEN 0 ELSE 1 END) = 1)),
        CONSTRAINT [FK_CartItems_Bundles_TenantId_BundleId] FOREIGN KEY ([TenantId], [BundleId]) REFERENCES [Bundles] ([TenantId], [Id]),
        CONSTRAINT [FK_CartItems_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_CartItems_LearningPaths_TenantId_LearningPathId] FOREIGN KEY ([TenantId], [LearningPathId]) REFERENCES [LearningPaths] ([TenantId], [Id]),
        CONSTRAINT [FK_CartItems_Lessons_TenantId_LessonId] FOREIGN KEY ([TenantId], [LessonId]) REFERENCES [Lessons] ([TenantId], [Id]),
        CONSTRAINT [FK_CartItems_SubscriptionPlans_TenantId_PlanId] FOREIGN KEY ([TenantId], [PlanId]) REFERENCES [SubscriptionPlans] ([TenantId], [Id]),
        CONSTRAINT [FK_CartItems_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_CartItems_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [DiscussionThreads] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [LessonId] uniqueidentifier NULL,
        [AuthorId] uniqueidentifier NOT NULL,
        [ThreadType] nvarchar(40) NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [IsPinned] bit NOT NULL,
        [IsLocked] bit NOT NULL,
        [UpvoteCount] int NOT NULL,
        [AcceptedPostId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_DiscussionThreads] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_DiscussionThreads_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_DiscussionThreads_ThreadType] CHECK ([ThreadType] IN ('LessonQA','Forum')),
        CONSTRAINT [FK_DiscussionThreads_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_DiscussionThreads_Lessons_TenantId_LessonId] FOREIGN KEY ([TenantId], [LessonId]) REFERENCES [Lessons] ([TenantId], [Id]),
        CONSTRAINT [FK_DiscussionThreads_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_DiscussionThreads_UserTenantMemberships_TenantId_AuthorId] FOREIGN KEY ([TenantId], [AuthorId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [InteractiveVideoQuizzes] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [LessonId] uniqueidentifier NOT NULL,
        [QuestionId] uniqueidentifier NOT NULL,
        [TimestampSeconds] int NOT NULL,
        [IsRequired] bit NOT NULL,
        [AllowSkipAfterAttempts] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_InteractiveVideoQuizzes] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_InteractiveVideoQuizzes_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_InteractiveVideoQuizzes_Lessons_TenantId_LessonId] FOREIGN KEY ([TenantId], [LessonId]) REFERENCES [Lessons] ([TenantId], [Id]),
        CONSTRAINT [FK_InteractiveVideoQuizzes_Questions_TenantId_QuestionId] FOREIGN KEY ([TenantId], [QuestionId]) REFERENCES [Questions] ([TenantId], [Id]),
        CONSTRAINT [FK_InteractiveVideoQuizzes_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [LessonBookmarks] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [LessonId] uniqueidentifier NOT NULL,
        [TimestampSeconds] int NOT NULL,
        [Label] nvarchar(200) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_LessonBookmarks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LessonBookmarks_Lessons_TenantId_LessonId] FOREIGN KEY ([TenantId], [LessonId]) REFERENCES [Lessons] ([TenantId], [Id]),
        CONSTRAINT [FK_LessonBookmarks_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_LessonBookmarks_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [LessonChapters] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [LessonId] uniqueidentifier NOT NULL,
        [Title] nvarchar(250) NOT NULL,
        [StartSeconds] int NOT NULL,
        [Source] nvarchar(40) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_LessonChapters] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_LessonChapters_Source] CHECK ([Source] IN ('Manual','Ai')),
        CONSTRAINT [FK_LessonChapters_Lessons_TenantId_LessonId] FOREIGN KEY ([TenantId], [LessonId]) REFERENCES [Lessons] ([TenantId], [Id]),
        CONSTRAINT [FK_LessonChapters_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [LessonIndexStates] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [LessonId] uniqueidentifier NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [ContentHash] nvarchar(64) NULL,
        [ChunkCount] int NULL,
        [IndexedAtUtc] datetime2 NULL,
        [LastError] nvarchar(1000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_LessonIndexStates] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_LessonIndexStates_Status] CHECK ([Status] IN ('NotIndexed','Pending','Processing','Indexed','Failed')),
        CONSTRAINT [FK_LessonIndexStates_Lessons_TenantId_LessonId] FOREIGN KEY ([TenantId], [LessonId]) REFERENCES [Lessons] ([TenantId], [Id]),
        CONSTRAINT [FK_LessonIndexStates_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [LessonNotes] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [LessonId] uniqueidentifier NOT NULL,
        [TimestampSeconds] int NULL,
        [Body] nvarchar(4000) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_LessonNotes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LessonNotes_Lessons_TenantId_LessonId] FOREIGN KEY ([TenantId], [LessonId]) REFERENCES [Lessons] ([TenantId], [Id]),
        CONSTRAINT [FK_LessonNotes_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_LessonNotes_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [LessonProgress] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [LessonId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [LastPositionSeconds] int NOT NULL,
        [WatchedSeconds] int NOT NULL,
        [PlaybackSpeed] float NULL,
        [CompletedAtUtc] datetime2 NULL,
        [LastAccessedAtUtc] datetime2 NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_LessonProgress] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_LessonProgress_Status] CHECK ([Status] IN ('NotStarted','InProgress','Completed')),
        CONSTRAINT [FK_LessonProgress_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_LessonProgress_Lessons_TenantId_LessonId] FOREIGN KEY ([TenantId], [LessonId]) REFERENCES [Lessons] ([TenantId], [Id]),
        CONSTRAINT [FK_LessonProgress_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_LessonProgress_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [LessonResources] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [LessonId] uniqueidentifier NOT NULL,
        [Title] nvarchar(250) NOT NULL,
        [FileId] uniqueidentifier NOT NULL,
        [IsDownloadable] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_LessonResources] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LessonResources_FileAssets_TenantId_FileId] FOREIGN KEY ([TenantId], [FileId]) REFERENCES [FileAssets] ([TenantId], [Id]),
        CONSTRAINT [FK_LessonResources_Lessons_TenantId_LessonId] FOREIGN KEY ([TenantId], [LessonId]) REFERENCES [Lessons] ([TenantId], [Id]),
        CONSTRAINT [FK_LessonResources_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [LessonSlides] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [LessonId] uniqueidentifier NOT NULL,
        [SortOrder] int NOT NULL,
        [ContentJson] nvarchar(max) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_LessonSlides] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_LessonSlides_ContentJson_Json] CHECK (ISJSON([ContentJson]) = 1),
        CONSTRAINT [FK_LessonSlides_Lessons_TenantId_LessonId] FOREIGN KEY ([TenantId], [LessonId]) REFERENCES [Lessons] ([TenantId], [Id]),
        CONSTRAINT [FK_LessonSlides_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [OrderItems] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [ItemType] nvarchar(40) NOT NULL,
        [CourseId] uniqueidentifier NULL,
        [LessonId] uniqueidentifier NULL,
        [BundleId] uniqueidentifier NULL,
        [PlanId] uniqueidentifier NULL,
        [LearningPathId] uniqueidentifier NULL,
        [Description] nvarchar(300) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [Quantity] int NOT NULL,
        [LineTotal] decimal(18,2) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_OrderItems] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_OrderItems_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_OrderItems_ItemType] CHECK ([ItemType] IN ('Course','Lesson','Bundle','Plan','Path','WalletTopUp')),
        CONSTRAINT [CK_OrderItems_Rule1] CHECK (((CASE WHEN [CourseId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [LessonId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [BundleId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [PlanId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [LearningPathId] IS NULL THEN 0 ELSE 1 END) = CASE WHEN [ItemType] = 'WalletTopUp' THEN 0 ELSE 1 END)),
        CONSTRAINT [FK_OrderItems_Bundles_TenantId_BundleId] FOREIGN KEY ([TenantId], [BundleId]) REFERENCES [Bundles] ([TenantId], [Id]),
        CONSTRAINT [FK_OrderItems_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_OrderItems_LearningPaths_TenantId_LearningPathId] FOREIGN KEY ([TenantId], [LearningPathId]) REFERENCES [LearningPaths] ([TenantId], [Id]),
        CONSTRAINT [FK_OrderItems_Lessons_TenantId_LessonId] FOREIGN KEY ([TenantId], [LessonId]) REFERENCES [Lessons] ([TenantId], [Id]),
        CONSTRAINT [FK_OrderItems_Orders_TenantId_OrderId] FOREIGN KEY ([TenantId], [OrderId]) REFERENCES [Orders] ([TenantId], [Id]),
        CONSTRAINT [FK_OrderItems_SubscriptionPlans_TenantId_PlanId] FOREIGN KEY ([TenantId], [PlanId]) REFERENCES [SubscriptionPlans] ([TenantId], [Id]),
        CONSTRAINT [FK_OrderItems_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [PlaybackSessions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [LessonId] uniqueidentifier NOT NULL,
        [DeviceId] uniqueidentifier NULL,
        [StartedAtUtc] datetime2 NOT NULL,
        [LastHeartbeatAtUtc] datetime2 NOT NULL,
        [EndedAtUtc] datetime2 NULL,
        [Ip] nvarchar(64) NULL,
        CONSTRAINT [PK_PlaybackSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PlaybackSessions_Lessons_TenantId_LessonId] FOREIGN KEY ([TenantId], [LessonId]) REFERENCES [Lessons] ([TenantId], [Id]),
        CONSTRAINT [FK_PlaybackSessions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_PlaybackSessions_UserDevices_UserId_DeviceId] FOREIGN KEY ([UserId], [DeviceId]) REFERENCES [UserDevices] ([UserId], [Id]),
        CONSTRAINT [FK_PlaybackSessions_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [StudySessions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NULL,
        [LessonId] uniqueidentifier NULL,
        [StartedAtUtc] datetime2 NOT NULL,
        [DurationSeconds] int NOT NULL,
        CONSTRAINT [PK_StudySessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StudySessions_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_StudySessions_Lessons_TenantId_LessonId] FOREIGN KEY ([TenantId], [LessonId]) REFERENCES [Lessons] ([TenantId], [Id]),
        CONSTRAINT [FK_StudySessions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_StudySessions_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [ExamAttemptQuestions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ExamAttemptId] uniqueidentifier NOT NULL,
        [QuestionId] uniqueidentifier NOT NULL,
        [SortOrder] int NOT NULL,
        [Points] float NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_ExamAttemptQuestions] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_ExamAttemptQuestions_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_ExamAttemptQuestions_ExamAttempts_TenantId_ExamAttemptId] FOREIGN KEY ([TenantId], [ExamAttemptId]) REFERENCES [ExamAttempts] ([TenantId], [Id]),
        CONSTRAINT [FK_ExamAttemptQuestions_Questions_TenantId_QuestionId] FOREIGN KEY ([TenantId], [QuestionId]) REFERENCES [Questions] ([TenantId], [Id]),
        CONSTRAINT [FK_ExamAttemptQuestions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [ProctoringEvents] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ExamAttemptId] uniqueidentifier NOT NULL,
        [EventType] nvarchar(40) NOT NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [DetailsJson] nvarchar(max) NULL,
        CONSTRAINT [PK_ProctoringEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProctoringEvents_DetailsJson_Json] CHECK (ISJSON([DetailsJson]) = 1),
        CONSTRAINT [CK_ProctoringEvents_EventType] CHECK ([EventType] IN ('TabSwitch','WindowBlur','FullscreenExit','CopyPaste','NetworkLoss','Other')),
        CONSTRAINT [FK_ProctoringEvents_ExamAttempts_TenantId_ExamAttemptId] FOREIGN KEY ([TenantId], [ExamAttemptId]) REFERENCES [ExamAttempts] ([TenantId], [Id]),
        CONSTRAINT [FK_ProctoringEvents_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [AssignmentGroups] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [AssignmentId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_AssignmentGroups] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_AssignmentGroups_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_AssignmentGroups_Assignments_TenantId_AssignmentId] FOREIGN KEY ([TenantId], [AssignmentId]) REFERENCES [Assignments] ([TenantId], [Id]),
        CONSTRAINT [FK_AssignmentGroups_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [AssignmentRubricCriteria] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [AssignmentId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [MaxPoints] float NOT NULL,
        [SortOrder] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_AssignmentRubricCriteria] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_AssignmentRubricCriteria_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_AssignmentRubricCriteria_Assignments_TenantId_AssignmentId] FOREIGN KEY ([TenantId], [AssignmentId]) REFERENCES [Assignments] ([TenantId], [Id]),
        CONSTRAINT [FK_AssignmentRubricCriteria_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [DiscussionPosts] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ThreadId] uniqueidentifier NOT NULL,
        [AuthorId] uniqueidentifier NOT NULL,
        [ParentPostId] uniqueidentifier NULL,
        [Body] nvarchar(max) NOT NULL,
        [IsInstructorPinned] bit NOT NULL,
        [UpvoteCount] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_DiscussionPosts] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_DiscussionPosts_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_DiscussionPosts_DiscussionPosts_TenantId_ParentPostId] FOREIGN KEY ([TenantId], [ParentPostId]) REFERENCES [DiscussionPosts] ([TenantId], [Id]),
        CONSTRAINT [FK_DiscussionPosts_DiscussionThreads_TenantId_ThreadId] FOREIGN KEY ([TenantId], [ThreadId]) REFERENCES [DiscussionThreads] ([TenantId], [Id]),
        CONSTRAINT [FK_DiscussionPosts_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_DiscussionPosts_UserTenantMemberships_TenantId_AuthorId] FOREIGN KEY ([TenantId], [AuthorId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [InteractiveQuizResponses] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [InteractiveVideoQuizId] uniqueidentifier NOT NULL,
        [AnswerJson] nvarchar(max) NOT NULL,
        [IsCorrect] bit NOT NULL,
        [AnsweredAtUtc] datetime2 NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_InteractiveQuizResponses] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_InteractiveQuizResponses_AnswerJson_Json] CHECK (ISJSON([AnswerJson]) = 1),
        CONSTRAINT [FK_InteractiveQuizResponses_InteractiveVideoQuizzes_TenantId_InteractiveVideoQuizId] FOREIGN KEY ([TenantId], [InteractiveVideoQuizId]) REFERENCES [InteractiveVideoQuizzes] ([TenantId], [Id]),
        CONSTRAINT [FK_InteractiveQuizResponses_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_InteractiveQuizResponses_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Enrollments] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [Source] nvarchar(40) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [EnrolledAtUtc] datetime2 NOT NULL,
        [ExpiresAtUtc] datetime2 NULL,
        [CompletedAtUtc] datetime2 NULL,
        [ProgressPercent] float NOT NULL,
        [OrderItemId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Enrollments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Enrollments_Source] CHECK ([Source] IN ('Free','Purchase','Voucher','Wallet','Subscription','Admin','Path')),
        CONSTRAINT [CK_Enrollments_Status] CHECK ([Status] IN ('Active','Completed','Expired','Revoked')),
        CONSTRAINT [FK_Enrollments_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_Enrollments_OrderItems_TenantId_OrderItemId] FOREIGN KEY ([TenantId], [OrderItemId]) REFERENCES [OrderItems] ([TenantId], [Id]),
        CONSTRAINT [FK_Enrollments_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_Enrollments_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [LessonAccessGrants] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [LessonId] uniqueidentifier NOT NULL,
        [Source] nvarchar(40) NOT NULL,
        [UnlockedAtUtc] datetime2 NOT NULL,
        [ExpiresAtUtc] datetime2 NULL,
        [OrderItemId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_LessonAccessGrants] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_LessonAccessGrants_Source] CHECK ([Source] IN ('PayPerLesson','Voucher','Wallet','Admin')),
        CONSTRAINT [FK_LessonAccessGrants_Lessons_TenantId_LessonId] FOREIGN KEY ([TenantId], [LessonId]) REFERENCES [Lessons] ([TenantId], [Id]),
        CONSTRAINT [FK_LessonAccessGrants_OrderItems_TenantId_OrderItemId] FOREIGN KEY ([TenantId], [OrderItemId]) REFERENCES [OrderItems] ([TenantId], [Id]),
        CONSTRAINT [FK_LessonAccessGrants_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_LessonAccessGrants_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [ExamAnswers] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ExamAttemptQuestionId] uniqueidentifier NOT NULL,
        [AnswerJson] nvarchar(max) NULL,
        [IsCorrect] bit NULL,
        [PointsAwarded] float NULL,
        [GradedBy] uniqueidentifier NULL,
        [GradedAtUtc] datetime2 NULL,
        [Feedback] nvarchar(2000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_ExamAnswers] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ExamAnswers_AnswerJson_Json] CHECK (ISJSON([AnswerJson]) = 1),
        CONSTRAINT [FK_ExamAnswers_ExamAttemptQuestions_TenantId_ExamAttemptQuestionId] FOREIGN KEY ([TenantId], [ExamAttemptQuestionId]) REFERENCES [ExamAttemptQuestions] ([TenantId], [Id]),
        CONSTRAINT [FK_ExamAnswers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_ExamAnswers_UserTenantMemberships_TenantId_GradedBy] FOREIGN KEY ([TenantId], [GradedBy]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [AssignmentGroupMembers] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [AssignmentGroupId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_AssignmentGroupMembers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AssignmentGroupMembers_AssignmentGroups_TenantId_AssignmentGroupId] FOREIGN KEY ([TenantId], [AssignmentGroupId]) REFERENCES [AssignmentGroups] ([TenantId], [Id]),
        CONSTRAINT [FK_AssignmentGroupMembers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_AssignmentGroupMembers_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [Submissions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [AssignmentId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NULL,
        [AssignmentGroupId] uniqueidentifier NULL,
        [Version] int NOT NULL,
        [TextContent] nvarchar(max) NULL,
        [LinkUrl] nvarchar(1000) NULL,
        [SubmittedAtUtc] datetime2 NOT NULL,
        [IsLate] bit NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [TotalScore] float NULL,
        [GradedBy] uniqueidentifier NULL,
        [GradedAtUtc] datetime2 NULL,
        [InstructorFeedback] nvarchar(max) NULL,
        [AiProvisionalScore] float NULL,
        [AiFeedback] nvarchar(max) NULL,
        [MaxSimilarity] float NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_Submissions] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Submissions_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Submissions_Rule1] CHECK ((([UserId] IS NOT NULL AND [AssignmentGroupId] IS NULL) OR ([UserId] IS NULL AND [AssignmentGroupId] IS NOT NULL))),
        CONSTRAINT [CK_Submissions_Status] CHECK ([Status] IN ('Submitted','UnderReview','Graded','ReturnedForResubmission')),
        CONSTRAINT [FK_Submissions_AssignmentGroups_TenantId_AssignmentGroupId] FOREIGN KEY ([TenantId], [AssignmentGroupId]) REFERENCES [AssignmentGroups] ([TenantId], [Id]),
        CONSTRAINT [FK_Submissions_Assignments_TenantId_AssignmentId] FOREIGN KEY ([TenantId], [AssignmentId]) REFERENCES [Assignments] ([TenantId], [Id]),
        CONSTRAINT [FK_Submissions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_Submissions_UserTenantMemberships_TenantId_GradedBy] FOREIGN KEY ([TenantId], [GradedBy]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId]),
        CONSTRAINT [FK_Submissions_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [DiscussionVotes] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [ThreadId] uniqueidentifier NULL,
        [PostId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_DiscussionVotes] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DiscussionVotes_Rule1] CHECK ((([ThreadId] IS NOT NULL AND [PostId] IS NULL) OR ([ThreadId] IS NULL AND [PostId] IS NOT NULL))),
        CONSTRAINT [FK_DiscussionVotes_DiscussionPosts_TenantId_PostId] FOREIGN KEY ([TenantId], [PostId]) REFERENCES [DiscussionPosts] ([TenantId], [Id]),
        CONSTRAINT [FK_DiscussionVotes_DiscussionThreads_TenantId_ThreadId] FOREIGN KEY ([TenantId], [ThreadId]) REFERENCES [DiscussionThreads] ([TenantId], [Id]),
        CONSTRAINT [FK_DiscussionVotes_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_DiscussionVotes_UserTenantMemberships_TenantId_UserId] FOREIGN KEY ([TenantId], [UserId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [PeerReviews] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SubmissionId] uniqueidentifier NOT NULL,
        [ReviewerId] uniqueidentifier NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [TotalScore] float NULL,
        [Comments] nvarchar(4000) NULL,
        [SubmittedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_PeerReviews] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_PeerReviews_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_PeerReviews_Status] CHECK ([Status] IN ('Assigned','Submitted','Skipped')),
        CONSTRAINT [FK_PeerReviews_Submissions_TenantId_SubmissionId] FOREIGN KEY ([TenantId], [SubmissionId]) REFERENCES [Submissions] ([TenantId], [Id]),
        CONSTRAINT [FK_PeerReviews_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_PeerReviews_UserTenantMemberships_TenantId_ReviewerId] FOREIGN KEY ([TenantId], [ReviewerId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [PlagiarismChecks] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SubmissionId] uniqueidentifier NOT NULL,
        [BackgroundJobId] uniqueidentifier NULL,
        [Status] nvarchar(40) NOT NULL,
        [MaxSimilarity] float NULL,
        [CheckedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_PlagiarismChecks] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_PlagiarismChecks_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_PlagiarismChecks_Status] CHECK ([Status] IN ('Pending','Processing','Completed','Failed')),
        CONSTRAINT [FK_PlagiarismChecks_BackgroundJobs_TenantId_BackgroundJobId] FOREIGN KEY ([TenantId], [BackgroundJobId]) REFERENCES [BackgroundJobs] ([TenantId], [Id]),
        CONSTRAINT [FK_PlagiarismChecks_Submissions_TenantId_SubmissionId] FOREIGN KEY ([TenantId], [SubmissionId]) REFERENCES [Submissions] ([TenantId], [Id]),
        CONSTRAINT [FK_PlagiarismChecks_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [SubmissionComments] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SubmissionId] uniqueidentifier NOT NULL,
        [AuthorId] uniqueidentifier NOT NULL,
        [AnchorJson] nvarchar(max) NULL,
        [Body] nvarchar(4000) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_SubmissionComments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_SubmissionComments_AnchorJson_Json] CHECK (ISJSON([AnchorJson]) = 1),
        CONSTRAINT [FK_SubmissionComments_Submissions_TenantId_SubmissionId] FOREIGN KEY ([TenantId], [SubmissionId]) REFERENCES [Submissions] ([TenantId], [Id]),
        CONSTRAINT [FK_SubmissionComments_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_SubmissionComments_UserTenantMemberships_TenantId_AuthorId] FOREIGN KEY ([TenantId], [AuthorId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [SubmissionCriterionScores] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SubmissionId] uniqueidentifier NOT NULL,
        [CriterionId] uniqueidentifier NOT NULL,
        [Points] float NOT NULL,
        [Feedback] nvarchar(2000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_SubmissionCriterionScores] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubmissionCriterionScores_AssignmentRubricCriteria_TenantId_CriterionId] FOREIGN KEY ([TenantId], [CriterionId]) REFERENCES [AssignmentRubricCriteria] ([TenantId], [Id]),
        CONSTRAINT [FK_SubmissionCriterionScores_Submissions_TenantId_SubmissionId] FOREIGN KEY ([TenantId], [SubmissionId]) REFERENCES [Submissions] ([TenantId], [Id]),
        CONSTRAINT [FK_SubmissionCriterionScores_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [SubmissionFiles] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SubmissionId] uniqueidentifier NOT NULL,
        [FileId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_SubmissionFiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubmissionFiles_FileAssets_TenantId_FileId] FOREIGN KEY ([TenantId], [FileId]) REFERENCES [FileAssets] ([TenantId], [Id]),
        CONSTRAINT [FK_SubmissionFiles_Submissions_TenantId_SubmissionId] FOREIGN KEY ([TenantId], [SubmissionId]) REFERENCES [Submissions] ([TenantId], [Id]),
        CONSTRAINT [FK_SubmissionFiles_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [PeerReviewScores] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [PeerReviewId] uniqueidentifier NOT NULL,
        [CriterionId] uniqueidentifier NOT NULL,
        [Points] float NOT NULL,
        [Comment] nvarchar(1000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_PeerReviewScores] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PeerReviewScores_AssignmentRubricCriteria_TenantId_CriterionId] FOREIGN KEY ([TenantId], [CriterionId]) REFERENCES [AssignmentRubricCriteria] ([TenantId], [Id]),
        CONSTRAINT [FK_PeerReviewScores_PeerReviews_TenantId_PeerReviewId] FOREIGN KEY ([TenantId], [PeerReviewId]) REFERENCES [PeerReviews] ([TenantId], [Id]),
        CONSTRAINT [FK_PeerReviewScores_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE TABLE [PlagiarismMatches] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [PlagiarismCheckId] uniqueidentifier NOT NULL,
        [OtherSubmissionId] uniqueidentifier NOT NULL,
        [Similarity] float NOT NULL,
        [ReviewedBy] uniqueidentifier NULL,
        [ReviewOutcome] nvarchar(40) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_PlagiarismMatches] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PlagiarismMatches_ReviewOutcome] CHECK ([ReviewOutcome] IN ('Unreviewed','Dismissed','Confirmed')),
        CONSTRAINT [FK_PlagiarismMatches_PlagiarismChecks_TenantId_PlagiarismCheckId] FOREIGN KEY ([TenantId], [PlagiarismCheckId]) REFERENCES [PlagiarismChecks] ([TenantId], [Id]),
        CONSTRAINT [FK_PlagiarismMatches_Submissions_TenantId_OtherSubmissionId] FOREIGN KEY ([TenantId], [OtherSubmissionId]) REFERENCES [Submissions] ([TenantId], [Id]),
        CONSTRAINT [FK_PlagiarismMatches_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_PlagiarismMatches_UserTenantMemberships_TenantId_ReviewedBy] FOREIGN KEY ([TenantId], [ReviewedBy]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AffiliateCommissions_TenantId_AffiliateUserId] ON [AffiliateCommissions] ([TenantId], [AffiliateUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AffiliateCommissions_TenantId_OrderId] ON [AffiliateCommissions] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AffiliateCommissions_TenantId_WalletTransactionId] ON [AffiliateCommissions] ([TenantId], [WalletTransactionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_AffiliateCommissions_AffiliateUserId_OrderId] ON [AffiliateCommissions] ([AffiliateUserId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AffiliateProfiles_TenantId_UserId] ON [AffiliateProfiles] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_AffiliateProfiles_TenantId_ReferralCode] ON [AffiliateProfiles] ([TenantId], [ReferralCode]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_AffiliateProfiles_UserId] ON [AffiliateProfiles] ([UserId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AiUsageLogs_TenantId_CourseId] ON [AiUsageLogs] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AiUsageLogs_TenantId_UserId] ON [AiUsageLogs] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AiUsageLogs_UserId_CreatedAtUtc] ON [AiUsageLogs] ([UserId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AnalyticsSnapshots_Kind_PeriodStartUtc] ON [AnalyticsSnapshots] ([Kind], [PeriodStartUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AnalyticsSnapshots_TenantId_CourseId] ON [AnalyticsSnapshots] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Announcements_TenantId_AuthorId] ON [Announcements] ([TenantId], [AuthorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Announcements_TenantId_CourseId] ON [Announcements] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ApiKeys_TenantId] ON [ApiKeys] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ApiKeys_KeyHash] ON [ApiKeys] ([KeyHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_AspNetUsers_PlatformUserName] ON [AspNetUsers] ([NormalizedUserName]) WHERE [IsDeleted] = 0 AND [TenantId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_AspNetUsers_TenantId_NormalizedUserName] ON [AspNetUsers] ([TenantId], [NormalizedUserName]) WHERE [IsDeleted] = 0 AND [TenantId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AssignmentGroupMembers_TenantId_AssignmentGroupId] ON [AssignmentGroupMembers] ([TenantId], [AssignmentGroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AssignmentGroupMembers_TenantId_UserId] ON [AssignmentGroupMembers] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_AssignmentGroupMembers_AssignmentGroupId_UserId] ON [AssignmentGroupMembers] ([AssignmentGroupId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AssignmentGroups_TenantId_AssignmentId] ON [AssignmentGroups] ([TenantId], [AssignmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AssignmentRubricCriteria_TenantId_AssignmentId] ON [AssignmentRubricCriteria] ([TenantId], [AssignmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Assignments_TenantId_CourseId] ON [Assignments] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Assignments_TenantId_LessonId] ON [Assignments] ([TenantId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EntityName_EntityId] ON [AuditLogs] ([EntityName], [EntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_TenantId_OccurredAtUtc] ON [AuditLogs] ([TenantId], [OccurredAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_UserId] ON [AuditLogs] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_BackgroundJobs_Status_Type] ON [BackgroundJobs] ([Status], [Type]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_BackgroundJobs_TenantId_RequestedBy] ON [BackgroundJobs] ([TenantId], [RequestedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Badges_TenantId_Code] ON [Badges] ([TenantId], [Code]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_BundleCourses_TenantId_BundleId] ON [BundleCourses] ([TenantId], [BundleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_BundleCourses_TenantId_CourseId] ON [BundleCourses] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_BundleCourses_BundleId_CourseId] ON [BundleCourses] ([BundleId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CartItems_TenantId_BundleId] ON [CartItems] ([TenantId], [BundleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CartItems_TenantId_CourseId] ON [CartItems] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CartItems_TenantId_LearningPathId] ON [CartItems] ([TenantId], [LearningPathId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CartItems_TenantId_LessonId] ON [CartItems] ([TenantId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CartItems_TenantId_PlanId] ON [CartItems] ([TenantId], [PlanId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CartItems_TenantId_UserId] ON [CartItems] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Categories_TenantId_ParentCategoryId] ON [Categories] ([TenantId], [ParentCategoryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Categories_TenantId_Slug] ON [Categories] ([TenantId], [Slug]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Certificates_TenantId_CourseId] ON [Certificates] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Certificates_TenantId_LearningPathId] ON [Certificates] ([TenantId], [LearningPathId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Certificates_TenantId_PdfFileId] ON [Certificates] ([TenantId], [PdfFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Certificates_TenantId_UserId] ON [Certificates] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Certificates_VerificationCode] ON [Certificates] ([VerificationCode]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ChatMessages_ChatRoomId_SentAtUtc] ON [ChatMessages] ([ChatRoomId], [SentAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ChatMessages_TenantId_ChatRoomId] ON [ChatMessages] ([TenantId], [ChatRoomId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ChatMessages_TenantId_SenderId] ON [ChatMessages] ([TenantId], [SenderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ChatRoomMembers_TenantId_ChatRoomId] ON [ChatRoomMembers] ([TenantId], [ChatRoomId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ChatRoomMembers_TenantId_UserId] ON [ChatRoomMembers] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ChatRoomMembers_ChatRoomId_UserId] ON [ChatRoomMembers] ([ChatRoomId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ChatRooms_TenantId_CourseId] ON [ChatRooms] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CouponRedemptions_TenantId_CouponId] ON [CouponRedemptions] ([TenantId], [CouponId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CouponRedemptions_TenantId_OrderId] ON [CouponRedemptions] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CouponRedemptions_TenantId_UserId] ON [CouponRedemptions] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_CouponRedemptions_CouponId_OrderId] ON [CouponRedemptions] ([CouponId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Coupons_TenantId_CourseId] ON [Coupons] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Coupons_TenantId_Code] ON [Coupons] ([TenantId], [Code]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CourseApprovals_TenantId_CourseId] ON [CourseApprovals] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CourseApprovals_TenantId_ReviewedBy] ON [CourseApprovals] ([TenantId], [ReviewedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CourseApprovals_TenantId_SubmittedBy] ON [CourseApprovals] ([TenantId], [SubmittedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CoursePrerequisites_TenantId_CourseId] ON [CoursePrerequisites] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CoursePrerequisites_TenantId_PrerequisiteCourseId] ON [CoursePrerequisites] ([TenantId], [PrerequisiteCourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_CoursePrerequisites_CourseId_PrerequisiteCourseId] ON [CoursePrerequisites] ([CourseId], [PrerequisiteCourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Courses_TenantId_CategoryId] ON [Courses] ([TenantId], [CategoryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Courses_TenantId_OwnerInstructorId] ON [Courses] ([TenantId], [OwnerInstructorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Courses_TenantId_Status] ON [Courses] ([TenantId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Courses_TenantId_ThumbnailFileId] ON [Courses] ([TenantId], [ThumbnailFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Courses_TenantId_Slug] ON [Courses] ([TenantId], [Slug]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CourseStaff_TenantId_CourseId] ON [CourseStaff] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CourseStaff_TenantId_UserId] ON [CourseStaff] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_CourseStaff_CourseId_UserId] ON [CourseStaff] ([CourseId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DeviceResetLogs_TenantId_ResetByUserId] ON [DeviceResetLogs] ([TenantId], [ResetByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DeviceResetLogs_TenantId_UserId] ON [DeviceResetLogs] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DeviceResetLogs_UserId_DeviceId] ON [DeviceResetLogs] ([UserId], [DeviceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiscussionPosts_TenantId_AuthorId] ON [DiscussionPosts] ([TenantId], [AuthorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiscussionPosts_TenantId_ParentPostId] ON [DiscussionPosts] ([TenantId], [ParentPostId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiscussionPosts_TenantId_ThreadId] ON [DiscussionPosts] ([TenantId], [ThreadId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiscussionThreads_CourseId_LessonId] ON [DiscussionThreads] ([CourseId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiscussionThreads_TenantId_AuthorId] ON [DiscussionThreads] ([TenantId], [AuthorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiscussionThreads_TenantId_CourseId] ON [DiscussionThreads] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiscussionThreads_TenantId_LessonId] ON [DiscussionThreads] ([TenantId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiscussionVotes_TenantId_PostId] ON [DiscussionVotes] ([TenantId], [PostId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiscussionVotes_TenantId_ThreadId] ON [DiscussionVotes] ([TenantId], [ThreadId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiscussionVotes_TenantId_UserId] ON [DiscussionVotes] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DropoutRiskScores_CourseId_UserId_ComputedAtUtc] ON [DropoutRiskScores] ([CourseId], [UserId], [ComputedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DropoutRiskScores_TenantId_CourseId] ON [DropoutRiskScores] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DropoutRiskScores_TenantId_UserId] ON [DropoutRiskScores] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Enrollments_CourseId_Status] ON [Enrollments] ([CourseId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Enrollments_TenantId_CourseId] ON [Enrollments] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Enrollments_TenantId_OrderItemId] ON [Enrollments] ([TenantId], [OrderItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Enrollments_TenantId_UserId] ON [Enrollments] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Enrollments_UserId_CourseId] ON [Enrollments] ([UserId], [CourseId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ExamAnswers_TenantId_ExamAttemptQuestionId] ON [ExamAnswers] ([TenantId], [ExamAttemptQuestionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ExamAnswers_TenantId_GradedBy] ON [ExamAnswers] ([TenantId], [GradedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ExamAnswers_ExamAttemptQuestionId] ON [ExamAnswers] ([ExamAttemptQuestionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ExamAttemptQuestions_TenantId_ExamAttemptId] ON [ExamAttemptQuestions] ([TenantId], [ExamAttemptId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ExamAttemptQuestions_TenantId_QuestionId] ON [ExamAttemptQuestions] ([TenantId], [QuestionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ExamAttempts_TenantId_ExamId] ON [ExamAttempts] ([TenantId], [ExamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ExamAttempts_TenantId_UserId] ON [ExamAttempts] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ExamAttempts_UserId_DeviceId] ON [ExamAttempts] ([UserId], [DeviceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ExamAttempts_ExamId_UserId_AttemptNumber] ON [ExamAttempts] ([ExamId], [UserId], [AttemptNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ExamQuestionRules_TenantId_ExamId] ON [ExamQuestionRules] ([TenantId], [ExamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ExamQuestionRules_TenantId_QuestionBankId] ON [ExamQuestionRules] ([TenantId], [QuestionBankId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ExamQuestionRules_TenantId_QuestionId] ON [ExamQuestionRules] ([TenantId], [QuestionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Exams_TenantId_CourseId] ON [Exams] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Exams_TenantId_SectionId] ON [Exams] ([TenantId], [SectionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_FileAssets_Sha256] ON [FileAssets] ([Sha256]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_FileAssets_TenantId_UploadedBy] ON [FileAssets] ([TenantId], [UploadedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_InstructorPayouts_TenantId_ApprovedBy] ON [InstructorPayouts] ([TenantId], [ApprovedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_InstructorPayouts_TenantId_InstructorId] ON [InstructorPayouts] ([TenantId], [InstructorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_InstructorRevenueShares_TenantId_CourseId] ON [InstructorRevenueShares] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_InstructorRevenueShares_TenantId_InstructorId] ON [InstructorRevenueShares] ([TenantId], [InstructorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_InstructorRevenueShares_CourseId_InstructorId] ON [InstructorRevenueShares] ([CourseId], [InstructorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_InteractiveQuizResponses_TenantId_InteractiveVideoQuizId] ON [InteractiveQuizResponses] ([TenantId], [InteractiveVideoQuizId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_InteractiveQuizResponses_TenantId_UserId] ON [InteractiveQuizResponses] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_InteractiveVideoQuizzes_LessonId_TimestampSeconds] ON [InteractiveVideoQuizzes] ([LessonId], [TimestampSeconds]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_InteractiveVideoQuizzes_TenantId_LessonId] ON [InteractiveVideoQuizzes] ([TenantId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_InteractiveVideoQuizzes_TenantId_QuestionId] ON [InteractiveVideoQuizzes] ([TenantId], [QuestionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Invoices_TenantId_OrderId] ON [Invoices] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Invoices_TenantId_PdfFileId] ON [Invoices] ([TenantId], [PdfFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Invoices_TenantId_InvoiceNumber] ON [Invoices] ([TenantId], [InvoiceNumber]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LearningPathCourses_TenantId_CourseId] ON [LearningPathCourses] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LearningPathCourses_TenantId_LearningPathId] ON [LearningPathCourses] ([TenantId], [LearningPathId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_LearningPathCourses_LearningPathId_CourseId] ON [LearningPathCourses] ([LearningPathId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_LearningPathCourses_LearningPathId_SortOrder] ON [LearningPathCourses] ([LearningPathId], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LearningPathEnrollments_TenantId_LearningPathId] ON [LearningPathEnrollments] ([TenantId], [LearningPathId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LearningPathEnrollments_TenantId_UserId] ON [LearningPathEnrollments] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_LearningPathEnrollments_UserId_LearningPathId] ON [LearningPathEnrollments] ([UserId], [LearningPathId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LearningPaths_TenantId_ThumbnailFileId] ON [LearningPaths] ([TenantId], [ThumbnailFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_LearningPaths_TenantId_Slug] ON [LearningPaths] ([TenantId], [Slug]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LessonAccessGrants_TenantId_LessonId] ON [LessonAccessGrants] ([TenantId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LessonAccessGrants_TenantId_OrderItemId] ON [LessonAccessGrants] ([TenantId], [OrderItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LessonAccessGrants_TenantId_UserId] ON [LessonAccessGrants] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_LessonAccessGrants_UserId_LessonId] ON [LessonAccessGrants] ([UserId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LessonBookmarks_TenantId_LessonId] ON [LessonBookmarks] ([TenantId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LessonBookmarks_TenantId_UserId] ON [LessonBookmarks] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LessonChapters_TenantId_LessonId] ON [LessonChapters] ([TenantId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LessonIndexStates_TenantId_LessonId] ON [LessonIndexStates] ([TenantId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_LessonIndexStates_LessonId] ON [LessonIndexStates] ([LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LessonNotes_TenantId_LessonId] ON [LessonNotes] ([TenantId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LessonNotes_TenantId_UserId] ON [LessonNotes] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LessonProgress_CourseId_UserId] ON [LessonProgress] ([CourseId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LessonProgress_TenantId_CourseId] ON [LessonProgress] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LessonProgress_TenantId_LessonId] ON [LessonProgress] ([TenantId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LessonProgress_TenantId_UserId] ON [LessonProgress] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_LessonProgress_UserId_LessonId] ON [LessonProgress] ([UserId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LessonResources_TenantId_FileId] ON [LessonResources] ([TenantId], [FileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LessonResources_TenantId_LessonId] ON [LessonResources] ([TenantId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Lessons_SectionId_SortOrder] ON [Lessons] ([SectionId], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Lessons_TenantId_ContentFileId] ON [Lessons] ([TenantId], [ContentFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Lessons_TenantId_CourseId] ON [Lessons] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Lessons_TenantId_PrerequisiteLessonId] ON [Lessons] ([TenantId], [PrerequisiteLessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Lessons_TenantId_SectionId] ON [Lessons] ([TenantId], [SectionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LessonSlides_TenantId_LessonId] ON [LessonSlides] ([TenantId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LiveSessionAttendance_TenantId_LiveSessionId] ON [LiveSessionAttendance] ([TenantId], [LiveSessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LiveSessionAttendance_TenantId_UserId] ON [LiveSessionAttendance] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_LiveSessionAttendance_LiveSessionId_UserId] ON [LiveSessionAttendance] ([LiveSessionId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LiveSessions_TenantId_CourseId] ON [LiveSessions] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LiveSessions_TenantId_HostId] ON [LiveSessions] ([TenantId], [HostId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ManualPaymentSubmissions_Status_TenantId] ON [ManualPaymentSubmissions] ([Status], [TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ManualPaymentSubmissions_TenantId_OrderId] ON [ManualPaymentSubmissions] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ManualPaymentSubmissions_TenantId_ReceiptFileId] ON [ManualPaymentSubmissions] ([TenantId], [ReceiptFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ManualPaymentSubmissions_TenantId_ReviewedBy] ON [ManualPaymentSubmissions] ([TenantId], [ReviewedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ManualPaymentSubmissions_TenantId_UserId] ON [ManualPaymentSubmissions] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ManualPaymentSubmissions_TenantId_WalletTransactionId] ON [ManualPaymentSubmissions] ([TenantId], [WalletTransactionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Notifications_TenantId_UserId] ON [Notifications] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Notifications_UserId_IsRead] ON [Notifications] ([UserId], [IsRead]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderItems_TenantId_BundleId] ON [OrderItems] ([TenantId], [BundleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderItems_TenantId_CourseId] ON [OrderItems] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderItems_TenantId_LearningPathId] ON [OrderItems] ([TenantId], [LearningPathId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderItems_TenantId_LessonId] ON [OrderItems] ([TenantId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderItems_TenantId_OrderId] ON [OrderItems] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderItems_TenantId_PlanId] ON [OrderItems] ([TenantId], [PlanId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_TenantId_CouponId] ON [Orders] ([TenantId], [CouponId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_TenantId_UserId] ON [Orders] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_UserId_Status] ON [Orders] ([UserId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OtpCodes_Destination_Purpose] ON [OtpCodes] ([Destination], [Purpose]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OtpCodes_TenantId] ON [OtpCodes] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OtpCodes_UserId] ON [OtpCodes] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ParentAlerts_ParentUserId_ReadAtUtc] ON [ParentAlerts] ([ParentUserId], [ReadAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ParentAlerts_TenantId_ParentUserId] ON [ParentAlerts] ([TenantId], [ParentUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ParentAlerts_TenantId_StudentUserId] ON [ParentAlerts] ([TenantId], [StudentUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ParentStudentLinks_TenantId_ApprovedBy] ON [ParentStudentLinks] ([TenantId], [ApprovedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ParentStudentLinks_TenantId_ParentUserId] ON [ParentStudentLinks] ([TenantId], [ParentUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ParentStudentLinks_TenantId_StudentUserId] ON [ParentStudentLinks] ([TenantId], [StudentUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ParentStudentLinks_ParentUserId_StudentUserId] ON [ParentStudentLinks] ([ParentUserId], [StudentUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Payments_GatewayReference] ON [Payments] ([GatewayReference]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Payments_TenantId_OrderId] ON [Payments] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Payments_IdempotencyKey] ON [Payments] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PeerReviews_TenantId_ReviewerId] ON [PeerReviews] ([TenantId], [ReviewerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PeerReviews_TenantId_SubmissionId] ON [PeerReviews] ([TenantId], [SubmissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_PeerReviews_SubmissionId_ReviewerId] ON [PeerReviews] ([SubmissionId], [ReviewerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PeerReviewScores_TenantId_CriterionId] ON [PeerReviewScores] ([TenantId], [CriterionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PeerReviewScores_TenantId_PeerReviewId] ON [PeerReviewScores] ([TenantId], [PeerReviewId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PlagiarismChecks_TenantId_BackgroundJobId] ON [PlagiarismChecks] ([TenantId], [BackgroundJobId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PlagiarismChecks_TenantId_SubmissionId] ON [PlagiarismChecks] ([TenantId], [SubmissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PlagiarismMatches_TenantId_OtherSubmissionId] ON [PlagiarismMatches] ([TenantId], [OtherSubmissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PlagiarismMatches_TenantId_PlagiarismCheckId] ON [PlagiarismMatches] ([TenantId], [PlagiarismCheckId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PlagiarismMatches_TenantId_ReviewedBy] ON [PlagiarismMatches] ([TenantId], [ReviewedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PlaybackSessions_TenantId_LessonId] ON [PlaybackSessions] ([TenantId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PlaybackSessions_TenantId_UserId] ON [PlaybackSessions] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PlaybackSessions_UserId_DeviceId] ON [PlaybackSessions] ([UserId], [DeviceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PlaybackSessions_UserId_LessonId] ON [PlaybackSessions] ([UserId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProctoringEvents_ExamAttemptId_OccurredAtUtc] ON [ProctoringEvents] ([ExamAttemptId], [OccurredAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProctoringEvents_TenantId_ExamAttemptId] ON [ProctoringEvents] ([TenantId], [ExamAttemptId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PushTokens_TenantId_UserId] ON [PushTokens] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PushTokens_UserId_DeviceId] ON [PushTokens] ([UserId], [DeviceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_PushTokens_UserId_FcmToken] ON [PushTokens] ([UserId], [FcmToken]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_QuestionBanks_TenantId_CourseId] ON [QuestionBanks] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_QuestionOptions_TenantId_QuestionId] ON [QuestionOptions] ([TenantId], [QuestionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Questions_QuestionBankId_Difficulty] ON [Questions] ([QuestionBankId], [Difficulty]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Questions_TenantId_QuestionBankId] ON [Questions] ([TenantId], [QuestionBankId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Referrals_TenantId_AffiliateUserId] ON [Referrals] ([TenantId], [AffiliateUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Referrals_TenantId_ReferredUserId] ON [Referrals] ([TenantId], [ReferredUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Referrals_ReferredUserId] ON [Referrals] ([ReferredUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_DeviceId] ON [RefreshTokens] ([DeviceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_TenantId] ON [RefreshTokens] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId] ON [RefreshTokens] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RefreshTokens_TokenHash] ON [RefreshTokens] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Sections_CourseId_SortOrder] ON [Sections] ([CourseId], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Sections_TenantId_CourseId] ON [Sections] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StudySessions_TenantId_CourseId] ON [StudySessions] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StudySessions_TenantId_LessonId] ON [StudySessions] ([TenantId], [LessonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StudySessions_TenantId_UserId] ON [StudySessions] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StudySessions_UserId_StartedAtUtc] ON [StudySessions] ([UserId], [StartedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SubmissionComments_TenantId_AuthorId] ON [SubmissionComments] ([TenantId], [AuthorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SubmissionComments_TenantId_SubmissionId] ON [SubmissionComments] ([TenantId], [SubmissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SubmissionCriterionScores_TenantId_CriterionId] ON [SubmissionCriterionScores] ([TenantId], [CriterionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SubmissionCriterionScores_TenantId_SubmissionId] ON [SubmissionCriterionScores] ([TenantId], [SubmissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_SubmissionCriterionScores_SubmissionId_CriterionId] ON [SubmissionCriterionScores] ([SubmissionId], [CriterionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SubmissionFiles_TenantId_FileId] ON [SubmissionFiles] ([TenantId], [FileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SubmissionFiles_TenantId_SubmissionId] ON [SubmissionFiles] ([TenantId], [SubmissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Submissions_TenantId_AssignmentGroupId] ON [Submissions] ([TenantId], [AssignmentGroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Submissions_TenantId_AssignmentId] ON [Submissions] ([TenantId], [AssignmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Submissions_TenantId_GradedBy] ON [Submissions] ([TenantId], [GradedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Submissions_TenantId_UserId] ON [Submissions] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SubscriptionPlanCourses_TenantId_CourseId] ON [SubscriptionPlanCourses] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SubscriptionPlanCourses_TenantId_PlanId] ON [SubscriptionPlanCourses] ([TenantId], [PlanId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_SubscriptionPlanCourses_PlanId_CourseId] ON [SubscriptionPlanCourses] ([PlanId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TenantFeatureFlags_TenantId_FeatureKey] ON [TenantFeatureFlags] ([TenantId], [FeatureKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Tenants_Slug] ON [Tenants] ([Slug]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Tenants_Subdomain] ON [Tenants] ([Subdomain]) WHERE [Subdomain] IS NOT NULL AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserBadges_TenantId_BadgeId] ON [UserBadges] ([TenantId], [BadgeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserBadges_TenantId_UserId] ON [UserBadges] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_UserBadges_UserId_BadgeId] ON [UserBadges] ([UserId], [BadgeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserDevices_RevokedBy] ON [UserDevices] ([RevokedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserDevices_TenantId] ON [UserDevices] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_UserDevices_UserId_DeviceFingerprintHash] ON [UserDevices] ([UserId], [DeviceFingerprintHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserStreaks_TenantId_UserId] ON [UserStreaks] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_UserStreaks_UserId] ON [UserStreaks] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserSubscriptions_TenantId_PlanId] ON [UserSubscriptions] ([TenantId], [PlanId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserSubscriptions_TenantId_UserId] ON [UserSubscriptions] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserSubscriptions_UserId_Status] ON [UserSubscriptions] ([UserId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserTenantMemberships_GrantedBy] ON [UserTenantMemberships] ([GrantedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserTenantMemberships_UserId] ON [UserTenantMemberships] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_VoucherBatches_TenantId_BackgroundJobId] ON [VoucherBatches] ([TenantId], [BackgroundJobId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_VoucherBatches_TenantId_CourseId] ON [VoucherBatches] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_VoucherBatches_TenantId_ExportFileId] ON [VoucherBatches] ([TenantId], [ExportFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Vouchers_TenantId_RedeemedBy] ON [Vouchers] ([TenantId], [RedeemedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Vouchers_TenantId_VoucherBatchId] ON [Vouchers] ([TenantId], [VoucherBatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Vouchers_VoucherBatchId_Status] ON [Vouchers] ([VoucherBatchId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Vouchers_TenantId_CodeHash] ON [Vouchers] ([TenantId], [CodeHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Wallets_TenantId_UserId] ON [Wallets] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Wallets_UserId] ON [Wallets] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WalletTransactions_TenantId_CreatedBy] ON [WalletTransactions] ([TenantId], [CreatedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WalletTransactions_TenantId_WalletId] ON [WalletTransactions] ([TenantId], [WalletId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WalletTransactions_WalletId_CreatedAtUtc] ON [WalletTransactions] ([WalletId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_WalletTransactions_WalletId_IdempotencyKey] ON [WalletTransactions] ([WalletId], [IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WebhookDeliveries_Status_NextAttemptAtUtc] ON [WebhookDeliveries] ([Status], [NextAttemptAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WebhookDeliveries_TenantId_BackgroundJobId] ON [WebhookDeliveries] ([TenantId], [BackgroundJobId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WebhookDeliveries_TenantId_WebhookEndpointId] ON [WebhookDeliveries] ([TenantId], [WebhookEndpointId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_XpTransactions_TenantId_UserId] ON [XpTransactions] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_XpTransactions_UserId_AwardedAtUtc] ON [XpTransactions] ([UserId], [AwardedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124133827_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261124133827_InitialCreate', N'10.0.15');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124161458_PeerReviewJobType'
)
BEGIN
    ALTER TABLE [BackgroundJobs] DROP CONSTRAINT [CK_BackgroundJobs_Type];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124161458_PeerReviewJobType'
)
BEGIN
    EXEC(N'ALTER TABLE [BackgroundJobs] ADD CONSTRAINT [CK_BackgroundJobs_Type] CHECK ([Type] IN (''AiIngest'',''AiTranscribe'',''PlagiarismBatch'',''VoucherGeneration'',''WebhookDelivery'',''CertificatePdf'',''AnalyticsRollup'',''Email'',''Export'',''PeerReviewAssign''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124161458_PeerReviewJobType'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261124161458_PeerReviewJobType', N'10.0.15');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124165207_PayoutJobType'
)
BEGIN
    ALTER TABLE [BackgroundJobs] DROP CONSTRAINT [CK_BackgroundJobs_Type];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124165207_PayoutJobType'
)
BEGIN
    EXEC(N'ALTER TABLE [BackgroundJobs] ADD CONSTRAINT [CK_BackgroundJobs_Type] CHECK ([Type] IN (''AiIngest'',''AiTranscribe'',''PlagiarismBatch'',''VoucherGeneration'',''WebhookDelivery'',''CertificatePdf'',''AnalyticsRollup'',''Email'',''Export'',''PeerReviewAssign'',''PayoutCalculation''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124165207_PayoutJobType'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261124165207_PayoutJobType', N'10.0.15');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124172031_TenantInstructorShare'
)
BEGIN
    ALTER TABLE [Tenants] ADD [InstructorSharePercent] float NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124172031_TenantInstructorShare'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261124172031_TenantInstructorShare', N'10.0.15');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    ALTER TABLE [Tenants] ADD [SubscriptionJumpAlertPercent] float NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    ALTER TABLE [Tenants] ADD [SubscriptionMinAvgLessonSeconds] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    ALTER TABLE [Tenants] ADD [SubscriptionMinWatchPercent] float NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    ALTER TABLE [FileAssets] ADD [MeasuredDurationSeconds] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    CREATE TABLE [CourseLearningWeights] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [NonVideoCourseSeconds] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_CourseLearningWeights] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CourseLearningWeights_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_CourseLearningWeights_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    CREATE TABLE [OrderItemAllocations] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderItemId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [ListPrice] decimal(18,2) NOT NULL,
        [NonVideoSeconds] int NULL,
        [NonVideoLessonCount] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_OrderItemAllocations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderItemAllocations_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_OrderItemAllocations_OrderItems_TenantId_OrderItemId] FOREIGN KEY ([TenantId], [OrderItemId]) REFERENCES [OrderItems] ([TenantId], [Id]),
        CONSTRAINT [FK_OrderItemAllocations_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    CREATE TABLE [OrderItemShares] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderItemId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [InstructorId] uniqueidentifier NOT NULL,
        [SharePercent] float NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_OrderItemShares] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderItemShares_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_OrderItemShares_OrderItems_TenantId_OrderItemId] FOREIGN KEY ([TenantId], [OrderItemId]) REFERENCES [OrderItems] ([TenantId], [Id]),
        CONSTRAINT [FK_OrderItemShares_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_OrderItemShares_UserTenantMemberships_TenantId_InstructorId] FOREIGN KEY ([TenantId], [InstructorId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    CREATE INDEX [IX_CourseLearningWeights_TenantId_CourseId] ON [CourseLearningWeights] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    CREATE UNIQUE INDEX [UX_CourseLearningWeights_CourseId] ON [CourseLearningWeights] ([CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderItemAllocations_TenantId_CourseId] ON [OrderItemAllocations] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderItemAllocations_TenantId_OrderItemId] ON [OrderItemAllocations] ([TenantId], [OrderItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    CREATE UNIQUE INDEX [UX_OrderItemAllocations_OrderItemId_CourseId] ON [OrderItemAllocations] ([OrderItemId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderItemShares_TenantId_CourseId] ON [OrderItemShares] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderItemShares_TenantId_InstructorId] ON [OrderItemShares] ([TenantId], [InstructorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    CREATE INDEX [IX_OrderItemShares_TenantId_OrderItemId] ON [OrderItemShares] ([TenantId], [OrderItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    CREATE UNIQUE INDEX [UX_OrderItemShares_OrderItemId_CourseId_InstructorId] ON [OrderItemShares] ([OrderItemId], [CourseId], [InstructorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124191318_SubscriptionAllocation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261124191318_SubscriptionAllocation', N'10.0.15');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    ALTER TABLE [OrderItemShares] ADD [ReleasedLate] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    ALTER TABLE [OrderItemShares] ADD [SettledPayoutId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    ALTER TABLE [InstructorPayouts] ADD CONSTRAINT [AK_InstructorPayouts_TenantId_Id] UNIQUE ([TenantId], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE TABLE [InstructorPayoutLines] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [InstructorPayoutId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [OrderItemId] uniqueidentifier NOT NULL,
        [CourseId] uniqueidentifier NOT NULL,
        [GrossEarned] decimal(18,2) NOT NULL,
        [PaidEarned] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_InstructorPayoutLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InstructorPayoutLines_Courses_TenantId_CourseId] FOREIGN KEY ([TenantId], [CourseId]) REFERENCES [Courses] ([TenantId], [Id]),
        CONSTRAINT [FK_InstructorPayoutLines_InstructorPayouts_TenantId_InstructorPayoutId] FOREIGN KEY ([TenantId], [InstructorPayoutId]) REFERENCES [InstructorPayouts] ([TenantId], [Id]),
        CONSTRAINT [FK_InstructorPayoutLines_OrderItems_TenantId_OrderItemId] FOREIGN KEY ([TenantId], [OrderItemId]) REFERENCES [OrderItems] ([TenantId], [Id]),
        CONSTRAINT [FK_InstructorPayoutLines_Orders_TenantId_OrderId] FOREIGN KEY ([TenantId], [OrderId]) REFERENCES [Orders] ([TenantId], [Id]),
        CONSTRAINT [FK_InstructorPayoutLines_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE TABLE [OrderRefunds] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [Reference] nvarchar(100) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Reason] nvarchar(500) NULL,
        [RecordedBy] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_OrderRefunds] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_OrderRefunds_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_OrderRefunds_Orders_TenantId_OrderId] FOREIGN KEY ([TenantId], [OrderId]) REFERENCES [Orders] ([TenantId], [Id]),
        CONSTRAINT [FK_OrderRefunds_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_OrderRefunds_UserTenantMemberships_TenantId_RecordedBy] FOREIGN KEY ([TenantId], [RecordedBy]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE TABLE [InstructorDebits] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [InstructorId] uniqueidentifier NOT NULL,
        [OrderRefundId] uniqueidentifier NOT NULL,
        [SourcePayoutId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [RemainingAmount] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_InstructorDebits] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_InstructorDebits_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_InstructorDebits_InstructorPayouts_TenantId_SourcePayoutId] FOREIGN KEY ([TenantId], [SourcePayoutId]) REFERENCES [InstructorPayouts] ([TenantId], [Id]),
        CONSTRAINT [FK_InstructorDebits_OrderRefunds_TenantId_OrderRefundId] FOREIGN KEY ([TenantId], [OrderRefundId]) REFERENCES [OrderRefunds] ([TenantId], [Id]),
        CONSTRAINT [FK_InstructorDebits_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
        CONSTRAINT [FK_InstructorDebits_UserTenantMemberships_TenantId_InstructorId] FOREIGN KEY ([TenantId], [InstructorId]) REFERENCES [UserTenantMemberships] ([TenantId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE TABLE [InstructorDebitApplications] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [InstructorDebitId] uniqueidentifier NOT NULL,
        [InstructorPayoutId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_InstructorDebitApplications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InstructorDebitApplications_InstructorDebits_TenantId_InstructorDebitId] FOREIGN KEY ([TenantId], [InstructorDebitId]) REFERENCES [InstructorDebits] ([TenantId], [Id]),
        CONSTRAINT [FK_InstructorDebitApplications_InstructorPayouts_TenantId_InstructorPayoutId] FOREIGN KEY ([TenantId], [InstructorPayoutId]) REFERENCES [InstructorPayouts] ([TenantId], [Id]),
        CONSTRAINT [FK_InstructorDebitApplications_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE INDEX [IX_OrderItemShares_TenantId_SettledPayoutId] ON [OrderItemShares] ([TenantId], [SettledPayoutId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE INDEX [IX_InstructorDebitApplications_InstructorPayoutId] ON [InstructorDebitApplications] ([InstructorPayoutId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE INDEX [IX_InstructorDebitApplications_TenantId_InstructorDebitId] ON [InstructorDebitApplications] ([TenantId], [InstructorDebitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE INDEX [IX_InstructorDebitApplications_TenantId_InstructorPayoutId] ON [InstructorDebitApplications] ([TenantId], [InstructorPayoutId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE INDEX [IX_InstructorDebits_InstructorId_RemainingAmount] ON [InstructorDebits] ([InstructorId], [RemainingAmount]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE INDEX [IX_InstructorDebits_TenantId_InstructorId] ON [InstructorDebits] ([TenantId], [InstructorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE INDEX [IX_InstructorDebits_TenantId_OrderRefundId] ON [InstructorDebits] ([TenantId], [OrderRefundId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE INDEX [IX_InstructorDebits_TenantId_SourcePayoutId] ON [InstructorDebits] ([TenantId], [SourcePayoutId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE INDEX [IX_InstructorPayoutLines_InstructorPayoutId] ON [InstructorPayoutLines] ([InstructorPayoutId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE INDEX [IX_InstructorPayoutLines_OrderId] ON [InstructorPayoutLines] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE INDEX [IX_InstructorPayoutLines_TenantId_CourseId] ON [InstructorPayoutLines] ([TenantId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE INDEX [IX_InstructorPayoutLines_TenantId_InstructorPayoutId] ON [InstructorPayoutLines] ([TenantId], [InstructorPayoutId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE INDEX [IX_InstructorPayoutLines_TenantId_OrderId] ON [InstructorPayoutLines] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE INDEX [IX_InstructorPayoutLines_TenantId_OrderItemId] ON [InstructorPayoutLines] ([TenantId], [OrderItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE INDEX [IX_OrderRefunds_TenantId_OrderId] ON [OrderRefunds] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE INDEX [IX_OrderRefunds_TenantId_RecordedBy] ON [OrderRefunds] ([TenantId], [RecordedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    CREATE UNIQUE INDEX [UX_OrderRefunds_OrderId_Reference] ON [OrderRefunds] ([OrderId], [Reference]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    ALTER TABLE [OrderItemShares] ADD CONSTRAINT [FK_OrderItemShares_InstructorPayouts_TenantId_SettledPayoutId] FOREIGN KEY ([TenantId], [SettledPayoutId]) REFERENCES [InstructorPayouts] ([TenantId], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124194607_RefundAccounting'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261124194607_RefundAccounting', N'10.0.15');
END;

COMMIT;
GO


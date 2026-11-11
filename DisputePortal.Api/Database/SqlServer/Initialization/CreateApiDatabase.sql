SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE SCHEMA [accounts];
GO
CREATE SCHEMA [audit];
GO
CREATE SCHEMA [configuration];
GO
CREATE SCHEMA [disputes];
GO
CREATE SCHEMA [identity];
GO
CREATE SCHEMA [integration];
GO
CREATE SCHEMA [ledger];
GO
CREATE SCHEMA [monitoring];
GO
CREATE SCHEMA [notifications];
GO
CREATE SCHEMA [support];
GO
CREATE SCHEMA [transactions];
GO
CREATE SEQUENCE [dbo].[internal_row_id_sequence]
    AS bigint
    START WITH 72
    INCREMENT BY 1
    MINVALUE -9223372036854775808
    MAXVALUE 9223372036854775807
    NO CYCLE;
GO
CREATE SEQUENCE [support].[messagesSequence]
    AS bigint
    START WITH 2
    INCREMENT BY 1
    MINVALUE -9223372036854775808
    MAXVALUE 9223372036854775807
    NO CYCLE;
GO
CREATE SEQUENCE [ledger].[projectionEntriesSequence]
    AS bigint
    START WITH 2
    INCREMENT BY 1
    MINVALUE -9223372036854775808
    MAXVALUE 9223372036854775807
    NO CYCLE;
GO
CREATE TABLE [accounts].[accounts] (
    [publicId] uniqueidentifier NOT NULL,
    [accountType] nvarchar(40) COLLATE Latin1_General_CI_AS NOT NULL,
    [currencyCode] char(3) COLLATE Latin1_General_CI_AS NOT NULL,
    [maskedAccountNumber] nvarchar(40) COLLATE Latin1_General_CI_AS NOT NULL,
    [accountStatus] nvarchar(20) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__accounts__accoun__438BFA74] DEFAULT ('Active'),
    [createdAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__accounts__create__44801EAD] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [protectedNumber] nvarchar(max) COLLATE Latin1_General_CI_AS NULL,
    [numberHash] varchar(64) COLLATE Latin1_General_CI_AS NULL,
    [deletedAt] datetimeoffset(7) NULL,
    [deletedById] bigint NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [userId] bigint NOT NULL,
    [countryPolicyVersion] int NOT NULL CONSTRAINT [df_accounts_countryPolicyVersion] DEFAULT ((0)),
    CONSTRAINT [PK__accounts__3213E83F8AF7A0C4] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__accounts__26B39585BB154742] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [accounts].[bankLinks] (
    [provider] varchar(60) COLLATE Latin1_General_CI_AS NOT NULL,
    [externalAccountHash] varchar(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [protectedExternalAccount] nvarchar(max) COLLATE Latin1_General_CI_AS NOT NULL,
    [protectedCursor] nvarchar(max) COLLATE Latin1_General_CI_AS NULL,
    [revision] int NOT NULL CONSTRAINT [DF__bankLinks__revis__3A02903A] DEFAULT ((1)),
    [lastSyncedAt] datetimeoffset(7) NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [accountId] bigint NOT NULL,
    CONSTRAINT [PK__bankLink__3213E83F13549A62] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [audit].[actorSnapshots] (
    [sourceKind] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL,
    [firstName] nvarchar(100) COLLATE Latin1_General_CI_AS NULL,
    [middleName] nvarchar(100) COLLATE Latin1_General_CI_AS NULL,
    [lastName] nvarchar(100) COLLATE Latin1_General_CI_AS NULL,
    [accountName] nvarchar(200) COLLATE Latin1_General_CI_AS NULL,
    [loginIdentifier] nvarchar(320) COLLATE Latin1_General_CI_AS NULL,
    [roleKeys] nvarchar(2000) COLLATE Latin1_General_CI_AS NULL,
    [actorKind] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL,
    [provenance] varchar(25) COLLATE Latin1_General_CI_AS NOT NULL,
    [capturedAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__actorSnap__captu__2D9CB955] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [actorUserId] bigint NULL,
    [auditEventId] bigint NULL,
    [automaticDecisionId] bigint NULL,
    [evidenceId] bigint NULL,
    [historyId] bigint NULL,
    [messageId] bigint NULL,
    [noteId] bigint NULL,
    [securityEventId] bigint NULL,
    CONSTRAINT [PK__actorSna__3213E83FB32E1422] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [audit].[auditEvents] (
    [publicId] uniqueidentifier NOT NULL,
    [action] nvarchar(100) COLLATE Latin1_General_CI_AS NOT NULL,
    [entityType] nvarchar(100) COLLATE Latin1_General_CI_AS NOT NULL,
    [entityPublicId] uniqueidentifier NULL,
    [occurredAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__auditEven__occur__316D4A39] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [correlationId] nvarchar(100) COLLATE Latin1_General_CI_AS NOT NULL,
    [safeMetadata] nvarchar(max) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__auditEven__safeM__32616E72] DEFAULT (N'{}'),
    [outcome] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__auditEven__outco__335592AB] DEFAULT ('Success'),
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [actorUserId] bigint NULL,
    CONSTRAINT [PK__auditEve__3213E83F05AF00A0] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__auditEve__26B39585C1F491AB] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [configuration].[disputeCategories] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [publicId] uniqueidentifier NOT NULL,
    [code] nvarchar(32) COLLATE Latin1_General_CI_AS NOT NULL,
    [label] nvarchar(200) COLLATE Latin1_General_CI_AS NOT NULL,
    [reason] nvarchar(1000) COLLATE Latin1_General_CI_AS NOT NULL,
    [sortOrder] int NOT NULL,
    [enabled] bit NOT NULL,
    CONSTRAINT [PK__disputeC__3213E83FA09207D0] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__disputeC__26B3958514A595E7] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [configuration].[disputeCategoryTransactionTypes] (
    [categoryId] bigint NOT NULL,
    [transactionTypeId] bigint NOT NULL,
    CONSTRAINT [pk_disputeCategoryTransactionTypes] PRIMARY KEY CLUSTERED ([categoryId] ASC, [transactionTypeId] ASC)
 );
GO
CREATE TABLE [configuration].[disputeInputPolicies] (
    [reasonMaximumLength] int NOT NULL,
    [descriptionMinimumLength] int NOT NULL,
    [descriptionMaximumLength] int NOT NULL,
    [commentMaximumLength] int NOT NULL,
    [noteMinimumLength] int NOT NULL,
    [noteMaximumLength] int NOT NULL,
    [statusMaximumLength] int NOT NULL,
    [idempotencyKeyMinimumLength] int NOT NULL,
    [idempotencyKeyMaximumLength] int NOT NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [publicId] uniqueidentifier NOT NULL CONSTRAINT [df_disputeInputPolicies_publicId] DEFAULT (newid()),
    [code] varchar(32) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [df_disputeInputPolicies_code] DEFAULT ('Default'),
    CONSTRAINT [pk_disputeInputPolicies] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [uq_disputeInputPolicies_code] UNIQUE NONCLUSTERED ([code] ASC),
    CONSTRAINT [uq_disputeInputPolicies_publicId] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [configuration].[evidenceCategories] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [settingsId] bigint NOT NULL,
    [deletedById] bigint NULL,
    [publicId] uniqueidentifier NOT NULL,
    [code] varchar(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [label] nvarchar(200) COLLATE Latin1_General_CI_AS NOT NULL,
    [enabled] bit NOT NULL,
    [sortOrder] int NOT NULL,
    [createdAt] datetimeoffset(7) NOT NULL,
    [deletedAt] datetimeoffset(7) NULL,
    CONSTRAINT [PK__evidence__3213E83F6116E792] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__evidence__26B39585780AD84B] UNIQUE NONCLUSTERED ([publicId] ASC),
    CONSTRAINT [UQ__evidence__357D4CF94E9B078E] UNIQUE NONCLUSTERED ([code] ASC)
 );
GO
CREATE TABLE [configuration].[evidenceFormats] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [publicId] uniqueidentifier NOT NULL,
    [extension] varchar(32) COLLATE Latin1_General_CI_AS NOT NULL,
    [contentType] varchar(100) COLLATE Latin1_General_CI_AS NOT NULL,
    [enabled] bit NOT NULL,
    [sortOrder] int NOT NULL,
    CONSTRAINT [PK__evidence__3213E83FD0DE302E] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__evidence__26B39585FD7F4E73] UNIQUE NONCLUSTERED ([publicId] ASC),
    CONSTRAINT [UQ__evidence__51EBE9109A995C4F] UNIQUE NONCLUSTERED ([extension] ASC)
 );
GO
CREATE TABLE [configuration].[evidencePolicies] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [settingsId] bigint NOT NULL,
    [publicId] uniqueidentifier NOT NULL,
    [code] varchar(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [version] int NOT NULL,
    [maximumFileBytes] int NOT NULL,
    [maximumOriginalBytes] int NOT NULL,
    [maximumFiles] int NOT NULL,
    [maximumCaseBytes] int NOT NULL,
    [maximumRequestBytes] int NOT NULL,
    [maximumFileNameLength] int NOT NULL,
    [minimumAccessHours] int NOT NULL,
    [maximumAccessHours] int NOT NULL,
    [defaultAccessHours] int NOT NULL,
    [accessReasonMinimumLength] int NOT NULL,
    [accessReasonMaximumLength] int NOT NULL,
    [accessDurations] nvarchar(max) COLLATE Latin1_General_CI_AS NOT NULL,
    CONSTRAINT [PK__evidence__3213E83F467AF397] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__evidence__26B395858E55BF74] UNIQUE NONCLUSTERED ([publicId] ASC),
    CONSTRAINT [UQ__evidence__357D4CF9300684D4] UNIQUE NONCLUSTERED ([code] ASC)
 );
GO
CREATE TABLE [configuration].[portalSettings] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [createdById] bigint NULL,
    [publicId] uniqueidentifier NOT NULL,
    [code] nvarchar(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [version] int NOT NULL,
    [body] nvarchar(max) COLLATE Latin1_General_CI_AS NOT NULL,
    [changeReason] nvarchar(500) COLLATE Latin1_General_CI_AS NOT NULL,
    [createdAt] datetimeoffset(7) NOT NULL,
    CONSTRAINT [PK__portalSe__3213E83FDA67CA40] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__portalSe__26B39585B8968E6A] UNIQUE NONCLUSTERED ([publicId] ASC),
    CONSTRAINT [uq_portalSettingVersions_code_version] UNIQUE NONCLUSTERED ([code] ASC, [version] ASC)
 );
GO
CREATE TABLE [configuration].[refundPolicies] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [policyId] bigint NOT NULL,
    [supersedesVersionId] bigint NULL,
    [createdById] bigint NULL,
    [deletedById] bigint NULL,
    [publicId] uniqueidentifier NOT NULL,
    [version] int NOT NULL,
    [priority] int NOT NULL CONSTRAINT [DF__refundPol__prior__6CAE0B98] DEFAULT ((0)),
    [effectiveFromUtc] datetimeoffset(7) NOT NULL,
    [effectiveToUtc] datetimeoffset(7) NULL,
    [publishedAtUtc] datetimeoffset(7) NULL,
    [createdAtUtc] datetimeoffset(7) NULL,
    [recordedAtUtc] datetimeoffset(7) NOT NULL CONSTRAINT [DF__refundPol__recor__6DA22FD1] DEFAULT (sysutcdatetime()),
    [changeReason] nvarchar(500) COLLATE Latin1_General_CI_AS NOT NULL,
    [isDeleted] bit NOT NULL CONSTRAINT [DF__refundPol__isDel__6E96540A] DEFAULT ((0)),
    [deletedAtUtc] datetimeoffset(7) NULL,
    [deletionReason] nvarchar(500) COLLATE Latin1_General_CI_AS NULL,
    [requestId] uniqueidentifier NULL,
    [requestFingerprint] varchar(64) COLLATE Latin1_General_CI_AS NULL,
    [sourceVersionPublicId] uniqueidentifier NULL,
    [provenance] varchar(32) COLLATE Latin1_General_CI_AS NOT NULL,
    [enabled] bit NOT NULL,
    [maximumAmount] decimal(18, 2) NOT NULL,
    [maximumAgeHours] int NOT NULL,
    [dailyLimit] decimal(18, 2) NOT NULL,
    [duplicateWindowMinutes] int NOT NULL,
    [configurationKey] bigint NULL,
    [initialVersion] int NULL,
    [code] varchar(64) COLLATE Latin1_General_CI_AS NULL,
    [scope] varchar(32) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__refundPol__scope__6F8A7843] DEFAULT ('Global'),
    [lastVersion] int NULL,
    [concurrencyToken] bigint NULL,
    CONSTRAINT [PK__refundPo__3213E83F11C67B72] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__refundPo__26B39585E0B19DE9] UNIQUE NONCLUSTERED ([publicId] ASC),
    CONSTRAINT [uq_refundPolicies_revision] UNIQUE NONCLUSTERED ([policyId] ASC, [version] ASC)
 );
GO
CREATE TABLE [configuration].[riskPolicies] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [policyId] bigint NOT NULL,
    [supersedesVersionId] bigint NULL,
    [createdById] bigint NULL,
    [deletedById] bigint NULL,
    [publicId] uniqueidentifier NOT NULL,
    [version] int NOT NULL,
    [priority] int NOT NULL CONSTRAINT [DF__riskPolic__prior__5A8F5B5D] DEFAULT ((0)),
    [effectiveFromUtc] datetimeoffset(7) NOT NULL,
    [effectiveToUtc] datetimeoffset(7) NULL,
    [publishedAtUtc] datetimeoffset(7) NULL,
    [createdAtUtc] datetimeoffset(7) NULL,
    [recordedAtUtc] datetimeoffset(7) NOT NULL CONSTRAINT [DF__riskPolic__recor__5B837F96] DEFAULT (sysutcdatetime()),
    [changeReason] nvarchar(500) COLLATE Latin1_General_CI_AS NOT NULL,
    [isDeleted] bit NOT NULL CONSTRAINT [DF__riskPolic__isDel__5C77A3CF] DEFAULT ((0)),
    [deletedAtUtc] datetimeoffset(7) NULL,
    [deletionReason] nvarchar(500) COLLATE Latin1_General_CI_AS NULL,
    [requestId] uniqueidentifier NULL,
    [requestFingerprint] varchar(64) COLLATE Latin1_General_CI_AS NULL,
    [sourceVersionPublicId] uniqueidentifier NULL,
    [provenance] varchar(32) COLLATE Latin1_General_CI_AS NOT NULL,
    [enabled] bit NOT NULL,
    [highValueThreshold] decimal(18, 2) NOT NULL,
    [extraCheckAfterDays] int NOT NULL,
    [debitOrderWindowDays] int NOT NULL,
    [debitOrderAppLimit] decimal(18, 2) NOT NULL,
    [configurationKey] bigint NULL,
    [initialVersion] int NULL,
    [code] varchar(64) COLLATE Latin1_General_CI_AS NULL,
    [scope] varchar(32) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__riskPolic__scope__5D6BC808] DEFAULT ('Global'),
    [lastVersion] int NULL,
    [concurrencyToken] bigint NULL,
    CONSTRAINT [PK__riskPoli__3213E83FD92F56F6] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__riskPoli__26B3958532D59649] UNIQUE NONCLUSTERED ([publicId] ASC),
    CONSTRAINT [uq_riskPolicies_revision] UNIQUE NONCLUSTERED ([policyId] ASC, [version] ASC)
 );
GO
CREATE TABLE [configuration].[transactionTypes] (
    [code] nvarchar(32) COLLATE Latin1_General_CI_AS NOT NULL,
    [displayName] varchar(80) COLLATE Latin1_General_CI_AS NOT NULL,
    [sortOrder] int NOT NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    CONSTRAINT [PK__transact__3213E83F62642673] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [dbo].[service_audit] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [actor] uniqueidentifier NULL,
    [action] varchar(100) COLLATE Latin1_General_CI_AS NOT NULL,
    [entity_id] uniqueidentifier NOT NULL,
    [detail] nvarchar(1000) COLLATE Latin1_General_CI_AS NOT NULL,
    [created_at] datetime2(7) NOT NULL CONSTRAINT [DF__service_a__creat__2BE97B0D] DEFAULT (sysutcdatetime()),
    CONSTRAINT [PK__service___3213E83FA951E130] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [dbo].[service_inbox] (
    [event_id] uniqueidentifier NOT NULL,
    [consumer_name] varchar(80) COLLATE Latin1_General_CI_AS NOT NULL,
    [event_type] varchar(80) COLLATE Latin1_General_CI_AS NOT NULL,
    [processed_at] datetime2(7) NOT NULL CONSTRAINT [DF__service_i__proce__290D0E62] DEFAULT (sysutcdatetime()),
    CONSTRAINT [PK__service___F0CCCD16B1058CF0] PRIMARY KEY CLUSTERED ([event_id] ASC, [consumer_name] ASC)
 );
GO
CREATE TABLE [dbo].[service_outbox] (
    [event_id] uniqueidentifier NOT NULL,
    [event_type] varchar(80) COLLATE Latin1_General_CI_AS NOT NULL,
    [event_version] int NOT NULL,
    [body] nvarchar(max) COLLATE Latin1_General_CI_AS NOT NULL,
    [correlation_id] uniqueidentifier NOT NULL,
    [state] varchar(16) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__service_o__state__24485945] DEFAULT ('Pending'),
    [attempts] int NOT NULL CONSTRAINT [DF__service_o__attem__253C7D7E] DEFAULT ((0)),
    [next_attempt_at] datetime2(7) NOT NULL,
    [lease_id] uniqueidentifier NULL,
    [lease_until] datetime2(7) NULL,
    [published_at] datetime2(7) NULL,
    [last_error] varchar(100) COLLATE Latin1_General_CI_AS NULL,
    [created_at] datetime2(7) NOT NULL CONSTRAINT [DF__service_o__creat__2630A1B7] DEFAULT (sysutcdatetime()),
    CONSTRAINT [PK__service___2370F7274FC453DF] PRIMARY KEY CLUSTERED ([event_id] ASC)
 );
GO
CREATE TABLE [disputes].[automaticDecisions] (
    [publicId] uniqueidentifier NOT NULL,
    [ruleCode] varchar(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [policyVersion] int NOT NULL,
    [outcome] varchar(32) COLLATE Latin1_General_CI_AS NOT NULL,
    [reason] nvarchar(500) COLLATE Latin1_General_CI_AS NOT NULL,
    [factsJson] nvarchar(max) COLLATE Latin1_General_CI_AS NOT NULL,
    [evaluatedAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__automatic__evalu__3726238F] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [disputeId] bigint NOT NULL,
    [refundPolicyVersionId] bigint NULL,
    CONSTRAINT [PK__automati__3213E83F4FB301EC] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__automati__26B39585CA2A10EE] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [disputes].[caseStatuses] (
    [code] nvarchar(24) COLLATE Latin1_General_CI_AS NOT NULL,
    [displayName] nvarchar(120) COLLATE Latin1_General_CI_AS NOT NULL,
    [sortOrder] int NOT NULL,
    [customerLabel] nvarchar(120) COLLATE Latin1_General_CI_AS NOT NULL,
    [tone] varchar(24) COLLATE Latin1_General_CI_AS NOT NULL,
    [stage] int NOT NULL,
    [nextStep] nvarchar(500) COLLATE Latin1_General_CI_AS NOT NULL,
    [ownerLabel] nvarchar(120) COLLATE Latin1_General_CI_AS NOT NULL,
    [steps] nvarchar(2000) COLLATE Latin1_General_CI_AS NOT NULL,
    [revision] bigint NOT NULL CONSTRAINT [DF__caseStatu__revis__42B7D1CC] DEFAULT ((1)),
    [updatedAtUtc] datetimeoffset(7) NOT NULL CONSTRAINT [DF__caseStatu__updat__44A01A3E] DEFAULT (sysutcdatetime()),
    CONSTRAINT [PK__caseStat__357D4CF8E5235D6E] PRIMARY KEY CLUSTERED ([code] ASC)
 );
GO
CREATE TABLE [disputes].[disputes] (
    [publicId] uniqueidentifier NOT NULL,
    [reference] nvarchar(40) COLLATE Latin1_General_CI_AS NOT NULL,
    [categoryId] bigint NOT NULL,
    [reason] nvarchar(1000) COLLATE Latin1_General_CI_AS NOT NULL,
    [description] nvarchar(2000) COLLATE Latin1_General_CI_AS NOT NULL,
    [status] nvarchar(24) COLLATE Latin1_General_CI_AS NOT NULL,
    [priority] smallint NOT NULL CONSTRAINT [DF__disputes__priori__444B1483] DEFAULT ((2)),
    [idempotencyKey] nvarchar(200) COLLATE Latin1_General_CI_AS NULL,
    [version] int NOT NULL CONSTRAINT [DF__disputes__versio__453F38BC] DEFAULT ((1)),
    [resolution] nvarchar(2000) COLLATE Latin1_General_CI_AS NULL,
    [resolvedAt] datetimeoffset(7) NULL,
    [createdAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__disputes__create__46335CF5] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [updatedAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__disputes__update__4727812E] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [queueType] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__disputes__queueT__481BA567] DEFAULT ('General'),
    [severity] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__disputes__severi__490FC9A0] DEFAULT ('Medium'),
    [assignedAt] datetimeoffset(7) NULL,
    [responseDueAt] datetimeoffset(7) NULL,
    [dueAt] datetimeoffset(7) NULL,
    [firstResponseAt] datetimeoffset(7) NULL,
    [slaClosedAt] datetimeoffset(7) NULL,
    [escalationLevel] int NOT NULL CONSTRAINT [DF__disputes__escala__4A03EDD9] DEFAULT ((0)),
    [escalatedAt] datetimeoffset(7) NULL,
    [escalationReason] nvarchar(500) COLLATE Latin1_General_CI_AS NULL,
    [workflowCycle] int NOT NULL CONSTRAINT [DF__disputes__workfl__4AF81212] DEFAULT ((1)),
    [specialistReview] bit NOT NULL CONSTRAINT [DF__disputes__specia__4BEC364B] DEFAULT ((0)),
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [assignedToId] bigint NULL,
    [customerId] bigint NOT NULL,
    [submittedById] bigint NULL,
    [transactionId] bigint NOT NULL,
    [autoAssignmentPaused] bit NOT NULL CONSTRAINT [DF__disputes__autoAs__4CE05A84] DEFAULT ((0)),
    [riskPolicyVersionId] bigint NULL,
    CONSTRAINT [PK__disputes__3213E83FB363DABB] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__disputes__26B3958577AB5AE2] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [disputes].[evidence] (
    [publicId] uniqueidentifier NOT NULL,
    [fileName] nvarchar(150) COLLATE Latin1_General_CI_AS NOT NULL,
    [contentType] varchar(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [sizeBytes] int NOT NULL,
    [sha256] char(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [protectedContent] nvarchar(max) COLLATE Latin1_General_CI_AS NOT NULL,
    [scanStatus] varchar(16) COLLATE Latin1_General_CI_AS NOT NULL,
    [createdAt] datetimeoffset(7) NOT NULL,
    [category] varchar(40) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__evidence__catego__5C57A83E] DEFAULT ('Other'),
    [originalFileName] nvarchar(150) COLLATE Latin1_General_CI_AS NULL,
    [originalContentType] varchar(64) COLLATE Latin1_General_CI_AS NULL,
    [originalSizeBytes] int NOT NULL CONSTRAINT [DF__evidence__origin__5D4BCC77] DEFAULT ((0)),
    [originalSha256] char(64) COLLATE Latin1_General_CI_AS NULL,
    [protectedOriginal] nvarchar(max) COLLATE Latin1_General_CI_AS NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [disputeId] bigint NOT NULL,
    [payloadId] bigint NULL,
    [uploadedById] bigint NOT NULL,
    [deletedById] bigint NULL,
    [deletedAt] datetimeoffset(7) NULL,
    [deletionReasonProtected] nvarchar(4000) COLLATE Latin1_General_CI_AS NULL,
    [categoryId] bigint NOT NULL,
    CONSTRAINT [PK__evidence__3213E83FEC8CD2F2] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__evidence__26B39585664250A0] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [disputes].[evidenceAccessGrants] (
    [publicId] uniqueidentifier NOT NULL,
    [reasonProtected] nvarchar(max) COLLATE Latin1_General_CI_AS NOT NULL,
    [createdAt] datetimeoffset(7) NOT NULL,
    [expiresAt] datetimeoffset(7) NOT NULL,
    [revokedAt] datetimeoffset(7) NULL,
    [revokeReasonProtected] nvarchar(max) COLLATE Latin1_General_CI_AS NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [evidenceId] bigint NOT NULL,
    [grantedById] bigint NOT NULL,
    [revokedById] bigint NULL,
    [userId] bigint NOT NULL,
    CONSTRAINT [PK__evidence__3213E83F96207E6F] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__evidence__26B395855C57C24C] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [disputes].[evidencePayloads] (
    [publicId] uniqueidentifier NOT NULL,
    [protectedContent] varbinary(max) NOT NULL,
    [protectedOriginal] varbinary(max) NULL,
    [createdAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__evidenceP__creat__7CC477D0] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [id] bigint IDENTITY(1, 1) NOT NULL,
    CONSTRAINT [PK__evidence__3213E83F36FA510E] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__evidence__26B39585EAB25075] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [disputes].[financialCommands] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [publicId] uniqueidentifier NOT NULL,
    [requestId] uniqueidentifier NOT NULL,
    [disputeId] bigint NOT NULL,
    [originalTransactionId] bigint NOT NULL,
    [accountId] bigint NOT NULL,
    [customerId] bigint NOT NULL,
    [kind] varchar(16) COLLATE Latin1_General_CI_AS NOT NULL,
    [amount] decimal(28, 8) NOT NULL,
    [currencyCode] char(3) COLLATE Latin1_General_CI_AS NOT NULL,
    [actorId] bigint NULL,
    [reason] nvarchar(1000) COLLATE Latin1_General_CI_AS NOT NULL,
    [policyVersion] int NULL,
    [expectedVersion] int NOT NULL,
    [correlation] uniqueidentifier NOT NULL,
    [state] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL,
    [failureCode] varchar(100) COLLATE Latin1_General_CI_AS NULL,
    [createdAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__financial__creat__611C5D5B] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [completedAt] datetimeoffset(7) NULL,
    [refundPolicyVersionId] bigint NULL,
    CONSTRAINT [PK__financia__3213E83FCE928A33] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__financia__26B39585A748C731] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [disputes].[notes] (
    [publicId] uniqueidentifier NOT NULL,
    [content] nvarchar(4000) COLLATE Latin1_General_CI_AS NOT NULL,
    [visibility] nvarchar(20) COLLATE Latin1_General_CI_AS NOT NULL,
    [createdAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__notes__createdAt__64ECEE3F] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [authorUserId] bigint NULL,
    [disputeId] bigint NOT NULL,
    CONSTRAINT [PK__notes__3213E83F5E4BCF1A] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__notes__26B39585B47D829B] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [disputes].[refundRecalls] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [publicId] uniqueidentifier NOT NULL,
    [disputeId] bigint NOT NULL,
    [creditTransactionId] bigint NOT NULL,
    [reversalTransactionId] bigint NOT NULL,
    [recalledById] bigint NOT NULL,
    [requestId] uniqueidentifier NOT NULL,
    [expectedVersion] int NOT NULL,
    [amount] decimal(28, 8) NOT NULL,
    [currencyCode] char(3) COLLATE Latin1_General_CI_AS NOT NULL,
    [reason] nvarchar(1000) COLLATE Latin1_General_CI_AS NOT NULL,
    [postedAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__refundRec__poste__68BD7F23] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    CONSTRAINT [PK__refundRe__3213E83FAF989C8A] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__refundRe__26B395858BDCB108] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [disputes].[refundReceipts] (
    [customerReference] varchar(11) COLLATE Latin1_General_CI_AS NOT NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [refundId] bigint NOT NULL,
    CONSTRAINT [PK__refundRe__3213E83F1C143444] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [disputes].[refunds] (
    [amount] decimal(28, 8) NOT NULL,
    [currencyCode] char(3) COLLATE Latin1_General_CI_AS NOT NULL,
    [requestId] uniqueidentifier NOT NULL,
    [reason] nvarchar(1000) COLLATE Latin1_General_CI_AS NOT NULL,
    [approvalSource] varchar(10) COLLATE Latin1_General_CI_AS NOT NULL,
    [policyVersion] int NULL,
    [postedAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__refunds__postedA__6B99EBCE] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [accountId] bigint NOT NULL,
    [approvedById] bigint NULL,
    [creditTransactionId] bigint NOT NULL,
    [customerId] bigint NOT NULL,
    [disputeId] bigint NOT NULL,
    [transactionId] bigint NOT NULL,
    [refundPolicyVersionId] bigint NULL,
    CONSTRAINT [PK__refunds__3213E83F86E8E997] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [disputes].[reinstatements] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [parentDisputeId] bigint NOT NULL,
    [childDisputeId] bigint NOT NULL,
    [customerId] bigint NOT NULL,
    [requestId] uniqueidentifier NOT NULL,
    [expectedVersion] int NOT NULL,
    [reason] nvarchar(1000) COLLATE Latin1_General_CI_AS NOT NULL,
    [createdAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__reinstate__creat__6E765879] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    CONSTRAINT [PK__reinstat__3213E83F785D32BA] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [disputes].[statusHistory] (
    [publicId] uniqueidentifier NOT NULL,
    [previousStatus] nvarchar(24) COLLATE Latin1_General_CI_AS NULL,
    [newStatus] nvarchar(24) COLLATE Latin1_General_CI_AS NOT NULL,
    [changedAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__statusHis__chang__7246E95D] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [comment] nvarchar(2000) COLLATE Latin1_General_CI_AS NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [actorUserId] bigint NULL,
    [disputeId] bigint NOT NULL,
    CONSTRAINT [PK__statusHi__3213E83F794D74FD] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__statusHi__26B3958568DAFB8C] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [identity].[accountDeletionChallenges] (
    [publicId] uniqueidentifier NOT NULL,
    [codeHash] varchar(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [state] varchar(15) COLLATE Latin1_General_CI_AS NOT NULL,
    [attempts] int NOT NULL CONSTRAINT [DF__accountDe__attem__26EFBBC6] DEFAULT ((0)),
    [createdAt] datetimeoffset(7) NOT NULL,
    [expiresAt] datetimeoffset(7) NOT NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [userId] bigint NOT NULL,
    CONSTRAINT [PK__accountD__3213E83FD0030A54] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__accountD__26B39585D3690639] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [identity].[accountDeletionRequests] (
    [publicId] uniqueidentifier NOT NULL,
    [reference] varchar(30) COLLATE Latin1_General_CI_AS NOT NULL,
    [status] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL,
    [version] int NOT NULL CONSTRAINT [DF__accountDe__versi__2AC04CAA] DEFAULT ((1)),
    [protectedReason] nvarchar(max) COLLATE Latin1_General_CI_AS NOT NULL,
    [protectedDecision] nvarchar(max) COLLATE Latin1_General_CI_AS NULL,
    [createdAt] datetimeoffset(7) NOT NULL,
    [reviewedAt] datetimeoffset(7) NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [reviewedById] bigint NULL,
    [userId] bigint NOT NULL,
    CONSTRAINT [PK__accountD__3213E83F8FB1245C] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__accountD__26B3958527AE722D] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [identity].[contactVerificationChallenges] (
    [publicId] uniqueidentifier NOT NULL,
    [purpose] varchar(30) COLLATE Latin1_General_CI_AS NOT NULL,
    [channel] varchar(10) COLLATE Latin1_General_CI_AS NOT NULL,
    [protectedDestination] nvarchar(2048) COLLATE Latin1_General_CI_AS NOT NULL,
    [codeHash] varchar(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [state] varchar(12) COLLATE Latin1_General_CI_AS NOT NULL,
    [attempts] int NOT NULL CONSTRAINT [DF__contactVe__attem__3FBB6990] DEFAULT ((0)),
    [createdAt] datetimeoffset(7) NOT NULL,
    [expiresAt] datetimeoffset(7) NOT NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [userId] bigint NOT NULL,
    CONSTRAINT [PK__contactV__3213E83F822792E6] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__contactV__26B395859936A477] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [identity].[customerProfiles] (
    [displayName] nvarchar(200) COLLATE Latin1_General_CI_AS NOT NULL,
    [maskedEmail] nvarchar(320) COLLATE Latin1_General_CI_AS NOT NULL,
    [maskedPhone] nvarchar(40) COLLATE Latin1_General_CI_AS NOT NULL,
    [createdAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__customerP__creat__475C8B58] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [updatedAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__customerP__updat__4850AF91] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [phone] varchar(40) COLLATE Latin1_General_CI_AS NULL,
    [email] nvarchar(320) COLLATE Latin1_General_CI_AS NULL,
    [emailVerified] bit NOT NULL CONSTRAINT [DF__customerP__email__4944D3CA] DEFAULT ((0)),
    [phoneVerified] bit NOT NULL CONSTRAINT [DF__customerP__phone__4A38F803] DEFAULT ((0)),
    [preferredOtpChannel] varchar(15) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__customerP__prefe__4B2D1C3C] DEFAULT ('email'),
    [firstName] nvarchar(100) COLLATE Latin1_General_CI_AS NULL,
    [lastName] nvarchar(100) COLLATE Latin1_General_CI_AS NULL,
    [protectedAddress] nvarchar(max) COLLATE Latin1_General_CI_AS NULL,
    [middleName] nvarchar(100) COLLATE Latin1_General_CI_AS NULL,
    [protectedIdentityNumber] nvarchar(max) COLLATE Latin1_General_CI_AS NULL,
    [identityLastFour] char(4) COLLATE Latin1_General_CI_AS NULL,
    [dateOfBirth] date NULL,
    [contactLoginEnabled] bit NOT NULL CONSTRAINT [DF__customerP__conta__4C214075] DEFAULT ((1)),
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [userId] bigint NOT NULL,
    [residenceCountry] char(2) COLLATE Latin1_General_CI_AS NULL,
    [phoneCountry] char(2) COLLATE Latin1_General_CI_AS NULL,
    [identityDocumentType] varchar(50) COLLATE Latin1_General_CI_AS NULL,
    [identityIssuerCountry] char(2) COLLATE Latin1_General_CI_AS NULL,
    [accountCurrency] char(3) COLLATE Latin1_General_CI_AS NULL,
    CONSTRAINT [PK__customer__3213E83F59A857F2] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [identity].[disputeVerifications] (
    [publicId] uniqueidentifier NOT NULL,
    [policyVersion] int NOT NULL,
    [channel] varchar(15) COLLATE Latin1_General_CI_AS NOT NULL,
    [codeHash] varchar(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [state] varchar(15) COLLATE Latin1_General_CI_AS NOT NULL,
    [attempts] int NOT NULL CONSTRAINT [DF__disputeVe__attem__76177A41] DEFAULT ((0)),
    [createdAt] datetimeoffset(7) NOT NULL,
    [expiresAt] datetimeoffset(7) NOT NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [sessionId] bigint NOT NULL,
    [transactionId] bigint NOT NULL,
    [userId] bigint NOT NULL,
    [riskPolicyVersionId] bigint NULL,
    CONSTRAINT [PK__disputeV__3213E83F9E38F667] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__disputeV__26B39585801BDAA0] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [identity].[identityChallenges] (
    [publicId] uniqueidentifier NOT NULL,
    [purpose] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL,
    [channel] varchar(15) COLLATE Latin1_General_CI_AS NOT NULL,
    [destinationHash] varchar(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [protectedPayload] nvarchar(max) COLLATE Latin1_General_CI_AS NOT NULL,
    [codeHash] varchar(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [state] varchar(12) COLLATE Latin1_General_CI_AS NOT NULL,
    [attempts] int NOT NULL CONSTRAINT [DF__identityC__attem__0559BDD1] DEFAULT ((0)),
    [createdAt] datetimeoffset(7) NOT NULL,
    [expiresAt] datetimeoffset(7) NOT NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [userId] bigint NULL,
    CONSTRAINT [PK__identity__3213E83FB44F4004] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__identity__26B39585185EBAAD] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [identity].[loginVerificationFlows] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [publicId] uniqueidentifier NOT NULL,
    [challengeId] bigint NOT NULL,
    [userId] bigint NOT NULL,
    [flowHash] varchar(200) COLLATE Latin1_General_CI_AS NOT NULL,
    [passwordStamp] varchar(200) COLLATE Latin1_General_CI_AS NOT NULL,
    [agentHash] varchar(200) COLLATE Latin1_General_CI_AS NOT NULL,
    [protectedDestination] nvarchar(max) COLLATE Latin1_General_CI_AS NOT NULL,
    [verifyRegisteredContact] bit NOT NULL,
    [createdAt] datetimeoffset(7) NOT NULL,
    [expiresAt] datetimeoffset(7) NOT NULL,
    [resendAvailableAt] datetimeoffset(7) NOT NULL,
    [resendCount] int NOT NULL CONSTRAINT [DF__loginVeri__resen__2042BE37] DEFAULT ((0)),
    [revision] int NOT NULL CONSTRAINT [DF__loginVeri__revis__222B06A9] DEFAULT ((0)),
    CONSTRAINT [pkLoginVerificationFlows] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [uqLoginVerificationFlowsPublicId] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [identity].[permissions] (
    [code] nvarchar(100) COLLATE Latin1_General_CI_AS NOT NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    CONSTRAINT [PK__permissi__3213E83F8663FA4D] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [identity].[rolePermissions] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [permissionId] bigint NOT NULL,
    [roleId] bigint NOT NULL,
    CONSTRAINT [PK__rolePerm__3213E83F5C90B850] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [identity].[roles] (
    [code] nvarchar(100) COLLATE Latin1_General_CI_AS NOT NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    CONSTRAINT [PK__roles__3213E83FA938A0FC] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [identity].[securityEvents] (
    [publicId] uniqueidentifier NOT NULL,
    [eventType] varchar(40) COLLATE Latin1_General_CI_AS NOT NULL,
    [outcome] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL,
    [ipAddress] varchar(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [userAgent] nvarchar(512) COLLATE Latin1_General_CI_AS NOT NULL,
    [deviceType] varchar(30) COLLATE Latin1_General_CI_AS NOT NULL,
    [browser] varchar(40) COLLATE Latin1_General_CI_AS NOT NULL,
    [location] nvarchar(120) COLLATE Latin1_General_CI_AS NOT NULL,
    [occurredAt] datetimeoffset(7) NOT NULL,
    [observedSessionPublicId] uniqueidentifier NULL,
    [correlationId] varchar(100) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__securityE__corre__28A2FA0E] DEFAULT (''),
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [userId] bigint NULL,
    CONSTRAINT [PK__security__3213E83FDCA3D238] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__security__26B3958591E3E250] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [identity].[trustedLoginDevices] (
    [publicId] uniqueidentifier NOT NULL,
    [tokenHash] varchar(200) COLLATE Latin1_General_CI_AS NOT NULL,
    [passwordStamp] varchar(200) COLLATE Latin1_General_CI_AS NOT NULL,
    [agentHash] varchar(200) COLLATE Latin1_General_CI_AS NOT NULL,
    [deviceType] varchar(30) COLLATE Latin1_General_CI_AS NOT NULL,
    [browser] varchar(40) COLLATE Latin1_General_CI_AS NOT NULL,
    [createdAt] datetimeoffset(7) NOT NULL,
    [expiresAt] datetimeoffset(7) NOT NULL,
    [lastUsedAt] datetimeoffset(7) NOT NULL,
    [revokedAt] datetimeoffset(7) NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [userId] bigint NOT NULL,
    CONSTRAINT [PK__trustedL__3213E83F945D0B5D] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__trustedL__26B39585199A2C46] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [identity].[userRoles] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [roleId] bigint NOT NULL,
    [userId] bigint NOT NULL,
    CONSTRAINT [PK__userRole__3213E83F59615040] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [identity].[users] (
    [publicId] uniqueidentifier NOT NULL,
    [loginIdentifier] nvarchar(320) COLLATE Latin1_General_CI_AS NOT NULL,
    [normalizedLoginIdentifier] nvarchar(320) COLLATE Latin1_General_CI_AS NOT NULL,
    [passwordHash] nvarchar(512) COLLATE Latin1_General_CI_AS NOT NULL,
    [accountStatus] nvarchar(20) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__users__accountSt__63C3BFDC] DEFAULT ('Active'),
    [createdAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__users__createdAt__64B7E415] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [updatedAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__users__updatedAt__65AC084E] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [otpSecret] varchar(2048) COLLATE Latin1_General_CI_AS NULL,
    [otpPendingSecret] varchar(2048) COLLATE Latin1_General_CI_AS NULL,
    [otpPendingExpires] datetimeoffset(7) NULL,
    [otpLastStep] bigint NOT NULL CONSTRAINT [DF__users__otpLastSt__66A02C87] DEFAULT ((-1)),
    [requiresSecondFactor] bit NOT NULL CONSTRAINT [DF__users__requiresS__679450C0] DEFAULT ((0)),
    [deletedAt] datetimeoffset(7) NULL,
    [deletedById] bigint NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [loginOtpEnabled] bit NULL,
    CONSTRAINT [PK__users__3213E83F57CCB33D] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__users__26B39585032FC5FB] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [identity].[userSessions] (
    [publicId] uniqueidentifier NOT NULL,
    [createdAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__userSessi__creat__5EFF0ABF] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [expiresAt] datetimeoffset(7) NOT NULL,
    [revokedAt] datetimeoffset(7) NULL,
    [ipAddress] varchar(64) COLLATE Latin1_General_CI_AS NULL,
    [deviceType] varchar(30) COLLATE Latin1_General_CI_AS NULL,
    [browser] varchar(40) COLLATE Latin1_General_CI_AS NULL,
    [lastActivityAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__userSessi__lastA__5FF32EF8] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [userId] bigint NOT NULL,
    CONSTRAINT [PK__userSess__3213E83FC6CDD6B9] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__userSess__26B39585B57E1D8E] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [integration].[accountMigrations] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [accountId] bigint NOT NULL,
    [snapshotId] uniqueidentifier NOT NULL,
    [sourceHash] char(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [countryCode] char(2) COLLATE Latin1_General_CI_AS NOT NULL,
    [state] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL,
    [capturedAt] datetimeoffset(7) NOT NULL,
    [activatedAt] datetimeoffset(7) NULL,
    CONSTRAINT [PK__accountM__3213E83FCFCAB6D1] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [integration].[bankingRegenerations] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [publicId] uniqueidentifier NOT NULL,
    [actorId] bigint NOT NULL,
    [fingerprint] varchar(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [protectedReason] nvarchar(max) COLLATE Latin1_General_CI_AS NOT NULL,
    [accountCount] int NOT NULL,
    [archivedCount] int NOT NULL,
    [createdCount] int NOT NULL,
    [completedAt] datetimeoffset(7) NOT NULL,
    CONSTRAINT [PK__bankingR__3213E83F26A40736] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__bankingR__26B39585E4288978] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [integration].[localBankingAccounts] (
    [openingRequestId] uniqueidentifier NOT NULL,
    [initialCount] int NOT NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [accountId] bigint NOT NULL,
    [bankingIdentityId] bigint NOT NULL,
    CONSTRAINT [PK__localBan__3213E83F9C82655D] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [integration].[localBankingIdentities] (
    [identityHash] varchar(64) COLLATE Latin1_General_CI_AS NULL,
    [createdAt] datetimeoffset(7) NOT NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [userId] bigint NOT NULL,
    CONSTRAINT [PK__localBan__3213E83F29D2AC17] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [integration].[localBankingOnboarding] (
    [completedAt] datetimeoffset(7) NOT NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [userId] bigint NOT NULL,
    CONSTRAINT [PK__localBan__3213E83F35370EC7] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [integration].[localTransactionBatches] (
    [publicId] uniqueidentifier NOT NULL,
    [transactionCount] int NOT NULL,
    [state] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL,
    [attempts] int NOT NULL CONSTRAINT [DF__localTran__attem__55AAAAAF] DEFAULT ((0)),
    [createdAt] datetimeoffset(7) NOT NULL,
    [completedAt] datetimeoffset(7) NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [bankingAccountId] bigint NOT NULL,
    [userId] bigint NOT NULL,
    CONSTRAINT [PK__localTra__3213E83FE8C94CAF] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__localTra__26B3958558788017] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [ledger].[projectionEntries] (
    [sequenceNo] bigint NOT NULL CONSTRAINT [DF__projectio__seque__009508B4] DEFAULT (NEXT VALUE FOR [ledger].[projectionEntriesSequence]),
    [currentDelta] decimal(28, 8) NOT NULL,
    [reservedDelta] decimal(28, 8) NOT NULL,
    [unknownDelta] smallint NOT NULL,
    [eventKind] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL,
    [recordedAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__projectio__recor__01892CED] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [accountId] bigint NOT NULL,
    [transactionId] bigint NOT NULL,
    CONSTRAINT [PK__projecti__3213E83F9027D742] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [monitoring].[apiMetricSummaries] (
    [bucketStart] datetimeoffset(7) NOT NULL,
    [httpMethod] varchar(12) COLLATE Latin1_General_CI_AS NOT NULL,
    [route] nvarchar(256) COLLATE Latin1_General_CI_AS NOT NULL,
    [statusCode] smallint NOT NULL,
    [requestCount] bigint NOT NULL,
    [failureCount] bigint NOT NULL,
    [clientErrorCount] bigint NOT NULL,
    [totalDurationMs] float NOT NULL,
    [minimumDurationMs] float NOT NULL,
    [maximumDurationMs] float NOT NULL,
    [updatedAt] datetimeoffset(7) NOT NULL,
    CONSTRAINT [PK_api_metric_summaries] PRIMARY KEY CLUSTERED ([bucketStart] ASC, [httpMethod] ASC, [route] ASC, [statusCode] ASC)
 );
GO
CREATE TABLE [monitoring].[metricBatches] (
    [publicId] uniqueidentifier NOT NULL,
    [createdAt] datetimeoffset(7) NOT NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    CONSTRAINT [pkMetricBatches] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [uqMetricBatchesPublicId] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [notifications].[deliveries] (
    [publicId] uniqueidentifier NOT NULL,
    [milestone] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL,
    [channel] varchar(15) COLLATE Latin1_General_CI_AS NOT NULL,
    [state] varchar(15) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__deliverie__state__0C06BB60] DEFAULT ('Pending'),
    [attempts] int NOT NULL CONSTRAINT [DF__deliverie__attem__0CFADF99] DEFAULT ((0)),
    [nextAttemptAt] datetimeoffset(7) NOT NULL,
    [leaseUntil] datetimeoffset(7) NULL,
    [lastError] varchar(100) COLLATE Latin1_General_CI_AS NULL,
    [createdAt] datetimeoffset(7) NOT NULL,
    [completedAt] datetimeoffset(7) NULL,
    [workflowCycle] int NOT NULL CONSTRAINT [DF__deliverie__workf__0DEF03D2] DEFAULT ((1)),
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [disputeId] bigint NOT NULL,
    [userId] bigint NOT NULL,
    CONSTRAINT [PK__deliveri__3213E83F1D917CE0] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__deliveri__26B39585C3281AD9] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [notifications].[events] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [publicId] uniqueidentifier NOT NULL,
    [eventKey] uniqueidentifier NOT NULL,
    [userId] bigint NOT NULL,
    [notificationId] bigint NOT NULL,
    [disputeId] bigint NULL,
    [kind] varchar(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [url] varchar(500) COLLATE Latin1_General_CI_AS NOT NULL,
    [state] varchar(16) COLLATE Latin1_General_CI_AS NOT NULL,
    [attempts] int NOT NULL CONSTRAINT [DF__events__attempts__11BF94B6] DEFAULT ((0)),
    [nextAttemptAt] datetimeoffset(7) NOT NULL,
    [leaseUntil] datetimeoffset(7) NULL,
    [lastError] varchar(100) COLLATE Latin1_General_CI_AS NULL,
    [createdAt] datetimeoffset(7) NOT NULL,
    [completedAt] datetimeoffset(7) NULL,
    [supportThreadId] bigint NULL,
    CONSTRAINT [PK__events__3213E83FC44F0A9C] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__events__26B395858CB66148] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [notifications].[notifications] (
    [publicId] uniqueidentifier NOT NULL,
    [notificationType] nvarchar(40) COLLATE Latin1_General_CI_AS NOT NULL,
    [title] nvarchar(200) COLLATE Latin1_General_CI_AS NOT NULL,
    [body] nvarchar(2000) COLLATE Latin1_General_CI_AS NOT NULL,
    [isRead] bit NOT NULL CONSTRAINT [DF__notificat__isRea__17786E0C] DEFAULT ((0)),
    [createdAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__notificat__creat__186C9245] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [disputeId] bigint NULL,
    [userId] bigint NOT NULL,
    CONSTRAINT [PK__notifica__3213E83F778B009D] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__notifica__26B39585B3F3F652] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [notifications].[preferences] (
    [channel] varchar(15) COLLATE Latin1_General_CI_AS NOT NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [userId] bigint NOT NULL,
    CONSTRAINT [PK__preferen__3213E83F020E7C00] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [notifications].[pushSubscriptions] (
    [publicId] uniqueidentifier NOT NULL,
    [endpointHash] varchar(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [protectedSubscription] nvarchar(max) COLLATE Latin1_General_CI_AS NOT NULL,
    [createdAt] datetimeoffset(7) NOT NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [sessionId] bigint NOT NULL,
    [userId] bigint NOT NULL,
    CONSTRAINT [PK__pushSubs__3213E83F69B16877] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__pushSubs__26B39585ABA149B5] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [support].[concerns] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [publicId] uniqueidentifier NOT NULL,
    [code] varchar(32) COLLATE Latin1_General_CI_AS NOT NULL,
    [label] nvarchar(100) COLLATE Latin1_General_CI_AS NOT NULL,
    [queueType] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL,
    [priority] int NOT NULL,
    [requiresCase] bit NOT NULL,
    [sortOrder] int NOT NULL,
    [enabled] bit NOT NULL,
    [contextKind] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__concerns__contex__30441BD6] DEFAULT ('None'),
    [collectDate] bit NOT NULL CONSTRAINT [DF__concerns__collec__3138400F] DEFAULT ((0)),
    CONSTRAINT [PK__concerns__3213E83F389A7663] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__concerns__26B3958554A33216] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [support].[conversations] (
    [publicId] uniqueidentifier NOT NULL,
    [subject] nvarchar(200) COLLATE Latin1_General_CI_AS NOT NULL,
    [createdAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__conversat__creat__3AC1AA49] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [customerId] bigint NOT NULL,
    [disputeId] bigint NULL,
    [transactionId] bigint NULL,
    [relatedDisputeId] bigint NULL,
    [relatedCaseContext] nvarchar(1000) COLLATE Latin1_General_CI_AS NULL,
    [concernId] bigint NOT NULL CONSTRAINT [DF__conversat__conce__3BB5CE82] DEFAULT ((1)),
    [conversationStatus] varchar(16) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__conversat__conve__3CA9F2BB] DEFAULT ('Open'),
    [closedAt] datetimeoffset(7) NULL,
    [closedById] bigint NULL,
    [closureReason] nvarchar(500) COLLATE Latin1_General_CI_AS NULL,
    [assignedToId] bigint NULL,
    [assignedAt] datetimeoffset(7) NULL,
    [queueType] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__conversat__queue__3D9E16F4] DEFAULT ('General'),
    [priority] int NOT NULL CONSTRAINT [DF__conversat__prior__3E923B2D] DEFAULT ((2)),
    [version] int NOT NULL CONSTRAINT [DF__conversat__versi__3F865F66] DEFAULT ((1)),
    [escalationLevel] int NOT NULL CONSTRAINT [DF__conversat__escal__407A839F] DEFAULT ((0)),
    CONSTRAINT [PK__conversa__3213E83FDB19380B] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__conversa__26B3958581328FBC] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [support].[messages] (
    [publicId] uniqueidentifier NOT NULL,
    [body] nvarchar(2000) COLLATE Latin1_General_CI_AS NOT NULL,
    [createdAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__messages__create__35FCF52C] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [sequenceNo] bigint NOT NULL CONSTRAINT [DF__messages__sequen__36F11965] DEFAULT (NEXT VALUE FOR [support].[messagesSequence]),
    [clientMessageId] uniqueidentifier NULL,
    [requestSubject] nvarchar(200) COLLATE Latin1_General_CI_AS NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [authorId] bigint NOT NULL,
    [threadId] bigint NOT NULL,
    [enquiryDetails] nvarchar(max) COLLATE Latin1_General_CI_AS NULL,
    [enquiryRequestHash] char(64) COLLATE Latin1_General_CI_AS NULL,
    CONSTRAINT [PK__messages__3213E83FB33B5447] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__messages__26B395851999B1CD] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
CREATE TABLE [support].[readStates] (
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [userId] bigint NOT NULL,
    [conversationId] bigint NOT NULL,
    [lastReadSequence] bigint NOT NULL CONSTRAINT [dfSupportReadSequence] DEFAULT ((0)),
    [openedAt] datetimeoffset(7) NOT NULL,
    CONSTRAINT [pkSupportReadStates] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [uqSupportReadState] UNIQUE NONCLUSTERED ([userId] ASC, [conversationId] ASC)
 );
GO
CREATE TABLE [support].[staffCapacity] (
    [availability] varchar(20) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF__staffCapa__avail__2B7F66B9] DEFAULT ('Offline'),
    [capacity] int NOT NULL CONSTRAINT [DF__staffCapa__capac__2C738AF2] DEFAULT ((5)),
    [lastAssignedAt] datetimeoffset(7) NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [userId] bigint NOT NULL,
    CONSTRAINT [PK__staffCap__3213E83F5282B50E] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [transactions].[bankLinks] (
    [externalTransactionHash] varchar(64) COLLATE Latin1_General_CI_AS NOT NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [accountId] bigint NOT NULL,
    [transactionId] bigint NOT NULL,
    CONSTRAINT [PK__bankLink__3213E83F822D8D5B] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [transactions].[duplicateConfirmations] (
    [confirmationSource] varchar(32) COLLATE Latin1_General_CI_AS NOT NULL,
    [confirmedAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__duplicate__confi__4FBCC72F] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [originalTransactionId] bigint NOT NULL,
    [transactionId] bigint NOT NULL,
    CONSTRAINT [PK__duplicat__3213E83F9CFBBE75] PRIMARY KEY CLUSTERED ([id] ASC)
 );
GO
CREATE TABLE [transactions].[transactions] (
    [publicId] uniqueidentifier NOT NULL,
    [transactionReference] nvarchar(100) COLLATE Latin1_General_CI_AS NOT NULL,
    [transactionAt] datetimeoffset(7) NOT NULL,
    [description] nvarchar(500) COLLATE Latin1_General_CI_AS NOT NULL,
    [merchantName] nvarchar(200) COLLATE Latin1_General_CI_AS NOT NULL,
    [amount] decimal(28, 8) NOT NULL,
    [currencyCode] char(3) COLLATE Latin1_General_CI_AS NOT NULL,
    [direction] nvarchar(10) COLLATE Latin1_General_CI_AS NOT NULL,
    [transactionStatus] nvarchar(20) COLLATE Latin1_General_CI_AS NOT NULL,
    [disputeEligible] bit NOT NULL CONSTRAINT [DF__transacti__dispu__5575A085] DEFAULT ((0)),
    [createdAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF__transacti__creat__5669C4BE] DEFAULT (todatetimeoffset(sysutcdatetime(),'+00:00')),
    [debitOrderScheme] varchar(20) COLLATE Latin1_General_CI_AS NULL,
    [mandateStatus] varchar(20) COLLATE Latin1_General_CI_AS NULL,
    [id] bigint IDENTITY(1, 1) NOT NULL,
    [accountId] bigint NOT NULL,
    [transactionTypeId] bigint NOT NULL,
    [archivedAt] datetimeoffset(7) NULL,
    CONSTRAINT [PK__transact__3213E83F7AB4A226] PRIMARY KEY CLUSTERED ([id] ASC),
    CONSTRAINT [UQ__transact__26B3958521CD55A4] UNIQUE NONCLUSTERED ([publicId] ASC)
 );
GO
SET IDENTITY_INSERT [configuration].[disputeCategories] ON;
GO
INSERT INTO [configuration].[disputeCategories] (
    [id],
    [publicId],
    [code],
    [label],
    [reason],
    [sortOrder],
    [enabled]
)
VALUES (
    N'1',
    N'7A4A5156-0EE5-5AF4-8B0A-B6EAE731E8EB',
    N'Unrecognized',
    N'I don’t recognise this transaction',
    N'I did not make this transaction',
    N'1',
    N'1'
);
INSERT INTO [configuration].[disputeCategories] (
    [id],
    [publicId],
    [code],
    [label],
    [reason],
    [sortOrder],
    [enabled]
)
VALUES (
    N'2',
    N'C7ECA02A-8E3F-51DB-B32F-FF77D4A43D1F',
    N'Duplicate',
    N'I was charged more than once',
    N'I was charged more than once',
    N'2',
    N'1'
);
INSERT INTO [configuration].[disputeCategories] (
    [id],
    [publicId],
    [code],
    [label],
    [reason],
    [sortOrder],
    [enabled]
)
VALUES (
    N'3',
    N'286AD7D6-FDE8-592A-A90B-743A1B3E2E59',
    N'IncorrectAmount',
    N'The amount looks incorrect',
    N'The amount is incorrect',
    N'3',
    N'1'
);
INSERT INTO [configuration].[disputeCategories] (
    [id],
    [publicId],
    [code],
    [label],
    [reason],
    [sortOrder],
    [enabled]
)
VALUES (
    N'4',
    N'E68C2349-8430-5105-8253-F79EC6BC272E',
    N'GoodsOrServices',
    N'I did not receive the goods or service',
    N'I did not receive the goods or service',
    N'4',
    N'1'
);
INSERT INTO [configuration].[disputeCategories] (
    [id],
    [publicId],
    [code],
    [label],
    [reason],
    [sortOrder],
    [enabled]
)
VALUES (
    N'5',
    N'37ED5876-9088-5512-9CE5-DD5C179928ED',
    N'DebitOrder',
    N'I cancelled this debit order',
    N'I cancelled this debit order',
    N'5',
    N'1'
);
INSERT INTO [configuration].[disputeCategories] (
    [id],
    [publicId],
    [code],
    [label],
    [reason],
    [sortOrder],
    [enabled]
)
VALUES (
    N'6',
    N'7CFB9D28-2C17-52E2-8A97-4501CD5FC120',
    N'Other',
    N'Something else',
    N'Other',
    N'6',
    N'1'
);
SET IDENTITY_INSERT [configuration].[disputeCategories] OFF;
GO
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'1',
    N'45'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'2',
    N'45'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'3',
    N'45'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'4',
    N'45'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'6',
    N'45'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'1',
    N'46'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'2',
    N'46'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'3',
    N'46'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'4',
    N'46'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'6',
    N'46'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'1',
    N'47'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'2',
    N'47'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'3',
    N'47'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'4',
    N'47'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'5',
    N'47'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'6',
    N'47'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'1',
    N'48'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'2',
    N'48'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'3',
    N'48'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'4',
    N'48'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'6',
    N'48'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'1',
    N'49'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'2',
    N'49'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'3',
    N'49'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'4',
    N'49'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'6',
    N'49'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'1',
    N'50'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'2',
    N'50'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'3',
    N'50'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'4',
    N'50'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'6',
    N'50'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'1',
    N'51'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'2',
    N'51'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'3',
    N'51'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'4',
    N'51'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'6',
    N'51'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'1',
    N'52'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'2',
    N'52'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'3',
    N'52'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'4',
    N'52'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'6',
    N'52'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'1',
    N'53'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'2',
    N'53'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'3',
    N'53'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'4',
    N'53'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'6',
    N'53'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'1',
    N'54'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'2',
    N'54'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'3',
    N'54'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'4',
    N'54'
);
INSERT INTO [configuration].[disputeCategoryTransactionTypes] (
    [categoryId],
    [transactionTypeId]
)
VALUES (
    N'6',
    N'54'
);
GO
SET IDENTITY_INSERT [configuration].[disputeInputPolicies] ON;
GO
INSERT INTO [configuration].[disputeInputPolicies] (
    [reasonMaximumLength],
    [descriptionMinimumLength],
    [descriptionMaximumLength],
    [commentMaximumLength],
    [noteMinimumLength],
    [noteMaximumLength],
    [statusMaximumLength],
    [idempotencyKeyMinimumLength],
    [idempotencyKeyMaximumLength],
    [id],
    [publicId],
    [code]
)
VALUES (
    N'120',
    N'10',
    N'2000',
    N'1000',
    N'1',
    N'4000',
    N'32',
    N'8',
    N'128',
    N'1',
    N'76AAFE91-2D2B-4827-8087-14316AA4C6E9',
    N'Default'
);
SET IDENTITY_INSERT [configuration].[disputeInputPolicies] OFF;
GO
SET IDENTITY_INSERT [configuration].[evidenceCategories] ON;
GO
INSERT INTO [configuration].[evidenceCategories] (
    [id],
    [settingsId],
    [deletedById],
    [publicId],
    [code],
    [label],
    [enabled],
    [sortOrder],
    [createdAt],
    [deletedAt]
)
VALUES (
    N'1',
    N'1',
    NULL,
    N'C2EA02A8-E732-471C-9F83-29B2E279910F',
    N'BankStatement',
    N'Bank Statement',
    N'1',
    N'1',
    N'2026-11-10T15:14:30.0970171Z',
    NULL
);
INSERT INTO [configuration].[evidenceCategories] (
    [id],
    [settingsId],
    [deletedById],
    [publicId],
    [code],
    [label],
    [enabled],
    [sortOrder],
    [createdAt],
    [deletedAt]
)
VALUES (
    N'2',
    N'1',
    NULL,
    N'E2384433-D1EB-4FBE-8F28-BF78E48405FF',
    N'CancellationConfirmation',
    N'Cancellation Confirmation',
    N'1',
    N'2',
    N'2026-11-10T15:14:30.0970171Z',
    NULL
);
INSERT INTO [configuration].[evidenceCategories] (
    [id],
    [settingsId],
    [deletedById],
    [publicId],
    [code],
    [label],
    [enabled],
    [sortOrder],
    [createdAt],
    [deletedAt]
)
VALUES (
    N'3',
    N'1',
    NULL,
    N'430102C5-7ED1-4BAD-8A2C-C5DBA9E4550C',
    N'FraudReport',
    N'Fraud Report',
    N'1',
    N'3',
    N'2026-11-10T15:14:30.0970171Z',
    NULL
);
INSERT INTO [configuration].[evidenceCategories] (
    [id],
    [settingsId],
    [deletedById],
    [publicId],
    [code],
    [label],
    [enabled],
    [sortOrder],
    [createdAt],
    [deletedAt]
)
VALUES (
    N'4',
    N'1',
    NULL,
    N'7A1AEC57-7160-41CA-BA28-2709D377BEBD',
    N'IdentityDocument',
    N'Identity Document',
    N'1',
    N'4',
    N'2026-11-10T15:14:30.0970171Z',
    NULL
);
INSERT INTO [configuration].[evidenceCategories] (
    [id],
    [settingsId],
    [deletedById],
    [publicId],
    [code],
    [label],
    [enabled],
    [sortOrder],
    [createdAt],
    [deletedAt]
)
VALUES (
    N'5',
    N'1',
    NULL,
    N'F995178F-8962-4A41-B777-F5576A3FAFCE',
    N'MandateOrAgreement',
    N'Mandate Or Agreement',
    N'1',
    N'5',
    N'2026-11-10T15:14:30.0970171Z',
    NULL
);
INSERT INTO [configuration].[evidenceCategories] (
    [id],
    [settingsId],
    [deletedById],
    [publicId],
    [code],
    [label],
    [enabled],
    [sortOrder],
    [createdAt],
    [deletedAt]
)
VALUES (
    N'6',
    N'1',
    NULL,
    N'34996F42-D223-466E-BB20-6B2A9A30F89E',
    N'MerchantCorrespondence',
    N'Merchant Correspondence',
    N'1',
    N'6',
    N'2026-11-10T15:14:30.0970171Z',
    NULL
);
INSERT INTO [configuration].[evidenceCategories] (
    [id],
    [settingsId],
    [deletedById],
    [publicId],
    [code],
    [label],
    [enabled],
    [sortOrder],
    [createdAt],
    [deletedAt]
)
VALUES (
    N'7',
    N'1',
    NULL,
    N'CBDB479D-B72A-48DB-A7BF-C079C4A6CD02',
    N'Other',
    N'Other Supporting Evidence',
    N'1',
    N'7',
    N'2026-11-10T15:14:30.0970171Z',
    NULL
);
INSERT INTO [configuration].[evidenceCategories] (
    [id],
    [settingsId],
    [deletedById],
    [publicId],
    [code],
    [label],
    [enabled],
    [sortOrder],
    [createdAt],
    [deletedAt]
)
VALUES (
    N'8',
    N'1',
    NULL,
    N'15E8E1DC-1D16-4AF0-A6F4-E1257584C484',
    N'PaidUpLetter',
    N'Paid-Up Letter',
    N'1',
    N'8',
    N'2026-11-10T15:14:30.0970171Z',
    NULL
);
INSERT INTO [configuration].[evidenceCategories] (
    [id],
    [settingsId],
    [deletedById],
    [publicId],
    [code],
    [label],
    [enabled],
    [sortOrder],
    [createdAt],
    [deletedAt]
)
VALUES (
    N'9',
    N'1',
    NULL,
    N'83FD78D0-57E0-4E14-9E55-C9D6A150F79E',
    N'PhotoOrScreenshot',
    N'Photo Or Screenshot',
    N'1',
    N'9',
    N'2026-11-10T15:14:30.0970171Z',
    NULL
);
INSERT INTO [configuration].[evidenceCategories] (
    [id],
    [settingsId],
    [deletedById],
    [publicId],
    [code],
    [label],
    [enabled],
    [sortOrder],
    [createdAt],
    [deletedAt]
)
VALUES (
    N'10',
    N'1',
    NULL,
    N'349EF1A7-4282-4F2B-827A-12917690DA23',
    N'ProofOfAddress',
    N'Proof Of Address',
    N'1',
    N'10',
    N'2026-11-10T15:14:30.0970171Z',
    NULL
);
INSERT INTO [configuration].[evidenceCategories] (
    [id],
    [settingsId],
    [deletedById],
    [publicId],
    [code],
    [label],
    [enabled],
    [sortOrder],
    [createdAt],
    [deletedAt]
)
VALUES (
    N'11',
    N'1',
    NULL,
    N'9F2B137C-A3AC-4301-A1E7-02452D09AEA3',
    N'ProofOfDelivery',
    N'Proof Of Delivery',
    N'1',
    N'11',
    N'2026-11-10T15:14:30.0970171Z',
    NULL
);
INSERT INTO [configuration].[evidenceCategories] (
    [id],
    [settingsId],
    [deletedById],
    [publicId],
    [code],
    [label],
    [enabled],
    [sortOrder],
    [createdAt],
    [deletedAt]
)
VALUES (
    N'12',
    N'1',
    NULL,
    N'4C2E0A8B-DD4A-4085-B255-8232BF431491',
    N'ProofOfPayment',
    N'Proof Of Payment',
    N'1',
    N'12',
    N'2026-11-10T15:14:30.0970171Z',
    NULL
);
INSERT INTO [configuration].[evidenceCategories] (
    [id],
    [settingsId],
    [deletedById],
    [publicId],
    [code],
    [label],
    [enabled],
    [sortOrder],
    [createdAt],
    [deletedAt]
)
VALUES (
    N'13',
    N'1',
    NULL,
    N'7B53E4C8-CFBB-4964-B393-F778445FFC1F',
    N'ReceiptOrInvoice',
    N'Receipt Or Invoice',
    N'1',
    N'13',
    N'2026-11-10T15:14:30.0970171Z',
    NULL
);
SET IDENTITY_INSERT [configuration].[evidenceCategories] OFF;
GO
SET IDENTITY_INSERT [configuration].[evidenceFormats] ON;
GO
INSERT INTO [configuration].[evidenceFormats] (
    [id],
    [publicId],
    [extension],
    [contentType],
    [enabled],
    [sortOrder]
)
VALUES (
    N'1',
    N'358FB359-3439-4BB0-93A4-52651652486F',
    N'.pdf',
    N'application/pdf',
    N'1',
    N'1'
);
INSERT INTO [configuration].[evidenceFormats] (
    [id],
    [publicId],
    [extension],
    [contentType],
    [enabled],
    [sortOrder]
)
VALUES (
    N'2',
    N'0D9D4303-7BCC-4194-979A-0D3635DFD4E1',
    N'.jpg',
    N'image/jpeg',
    N'1',
    N'2'
);
INSERT INTO [configuration].[evidenceFormats] (
    [id],
    [publicId],
    [extension],
    [contentType],
    [enabled],
    [sortOrder]
)
VALUES (
    N'3',
    N'1900F3AE-6681-4555-8445-33906FE22498',
    N'.jpeg',
    N'image/jpeg',
    N'1',
    N'3'
);
INSERT INTO [configuration].[evidenceFormats] (
    [id],
    [publicId],
    [extension],
    [contentType],
    [enabled],
    [sortOrder]
)
VALUES (
    N'4',
    N'EBE069D1-F19F-4110-A0CE-50D82BDE6643',
    N'.png',
    N'image/png',
    N'1',
    N'4'
);
INSERT INTO [configuration].[evidenceFormats] (
    [id],
    [publicId],
    [extension],
    [contentType],
    [enabled],
    [sortOrder]
)
VALUES (
    N'5',
    N'73759B70-DD38-475A-81C6-EA2D74079973',
    N'.webp',
    N'image/webp',
    N'1',
    N'5'
);
SET IDENTITY_INSERT [configuration].[evidenceFormats] OFF;
GO
SET IDENTITY_INSERT [configuration].[evidencePolicies] ON;
GO
INSERT INTO [configuration].[evidencePolicies] (
    [id],
    [settingsId],
    [publicId],
    [code],
    [version],
    [maximumFileBytes],
    [maximumOriginalBytes],
    [maximumFiles],
    [maximumCaseBytes],
    [maximumRequestBytes],
    [maximumFileNameLength],
    [minimumAccessHours],
    [maximumAccessHours],
    [defaultAccessHours],
    [accessReasonMinimumLength],
    [accessReasonMaximumLength],
    [accessDurations]
)
VALUES (
    N'1',
    N'1',
    N'C1ABDE01-FCCA-4042-A64B-4F98C935BF92',
    N'Default',
    N'1',
    N'5242880',
    N'20971520',
    N'10',
    N'26214400',
    N'27262976',
    N'150',
    N'1',
    N'168',
    N'24',
    N'5',
    N'500',
    N'{
    "1": "1 Hour",
    "8": "8 Hours",
    "24": "24 Hours",
    "72": "3 Days",
    "168": "7 Days"
  }'
);
SET IDENTITY_INSERT [configuration].[evidencePolicies] OFF;
GO
SET IDENTITY_INSERT [configuration].[portalSettings] ON;
GO
INSERT INTO [configuration].[portalSettings] (
    [id],
    [createdById],
    [publicId],
    [code],
    [version],
    [body],
    [changeReason],
    [createdAt]
)
VALUES (
    N'1',
    NULL,
    N'B74A3C04-AFA5-4AD2-A53B-3BFA3267A54E',
    N'Interface',
    N'1',
    N'{
  "roles": {
    "service_clerk": {
      "label": "Service Clerk",
      "description": "View customer and transaction details and submit disputes on behalf of clients. No refund or specialist assignment rights.",
      "rank": 7
    },
    "customer": {
      "label": "Customer",
      "description": "Own accounts, transactions and disputes.",
      "rank": 9
    },
    "dispute_agent": {
      "label": "Dispute Agent",
      "description": "General dispute reviews and customer replies.",
      "rank": 6
    },
    "fraud_agent": {
      "label": "Fraud Specialist",
      "description": "Unauthorised activity and fraud investigations.",
      "rank": 2
    },
    "card_specialist": {
      "label": "Card Specialist",
      "description": "Card purchases, duplicates and merchant disputes.",
      "rank": 3
    },
    "debit_order_specialist": {
      "label": "Debit-Order Specialist",
      "description": "Mandates, collections and cancellation disputes.",
      "rank": 4
    },
    "payments_specialist": {
      "label": "Payments Investigator",
      "description": "Transfer and cash-withdrawal investigations.",
      "rank": 5
    },
    "operations_manager": {
      "label": "Operations Manager",
      "description": "Assign cases, manage priorities and supervise reviews.",
      "rank": 1
    },
    "super_admin": {
      "label": "Super Admin",
      "description": "Manage users, roles, dispute rules and case assignments.",
      "rank": 0
    },
    "audit_reader": {
      "label": "Audit Reader",
      "description": "Read cases and audit history without changing them.",
      "rank": 8
    }
  },
  "queues": {
    "General": {
      "label": "Dispute Support",
      "steps": [
        "Confirm the transaction and customer concern",
        "Request any missing evidence",
        "Record the decision and customer outcome"
      ]
    },
    "Fraud": {
      "label": "Fraud Investigation",
      "steps": [
        "Review unauthorised activity and account risk",
        "Coordinate account protection through approved bank channels",
        "Document investigation evidence and the outcome"
      ]
    },
    "Card": {
      "label": "Card Disputes",
      "steps": [
        "Check the card transaction and merchant evidence",
        "Assess duplicate, amount or goods-and-services claims",
        "Record the scheme review outcome without promising a refund"
      ]
    },
    "DebitOrder": {
      "label": "Debit-Order Disputes",
      "steps": [
        "Review the mandate and cancellation evidence",
        "Check collection date and applicable dispute policy",
        "Record the specialist decision and explain the outcome"
      ]
    },
    "Payments": {
      "label": "Payments Investigations",
      "steps": [
        "Trace the transfer or cash withdrawal reference",
        "Reconcile settlement or cash-dispensing evidence",
        "Record the investigation result and customer outcome"
      ]
    }
  },
  "priorities": {
    "1": "Low",
    "2": "Normal",
    "3": "Medium",
    "4": "High",
    "5": "Critical"
  },
  "severities": [
    "Low",
    "Medium",
    "High",
    "Critical"
  ],
  "transactionStatuses": {
    "Posted": "Completed",
    "Pending": "Pending Payment",
    "Reversed": "Reversed"
  },
  "availability": [
    "Available",
    "Busy",
    "Offline"
  ],
  "staffStatuses": [
    "Active",
    "Locked",
    "Disabled"
  ],
  "evidenceDurations": {
    "1": "1 Hour",
    "8": "8 Hours",
    "24": "24 Hours",
    "72": "3 Days",
    "168": "7 Days"
  },
  "evidenceDefaultHours": 24,
  "paymentStatuses": {
    "AwaitingApproval": "Awaiting External Confirmation",
    "Preparing": "Preparing",
    "Processing": "Processing",
    "Completed": "Completed",
    "Failed": "Failed",
    "Cancelled": "Cancelled"
  },
  "EvidenceCategories": {
    "IdentityDocument": "Identity Document",
    "ProofOfAddress": "Proof Of Address",
    "PaidUpLetter": "Paid-Up Letter",
    "ProofOfPayment": "Proof Of Payment",
    "BankStatement": "Bank Statement",
    "ReceiptOrInvoice": "Receipt Or Invoice",
    "MerchantCorrespondence": "Merchant Correspondence",
    "CancellationConfirmation": "Cancellation Confirmation",
    "PhotoOrScreenshot": "Photo Or Screenshot",
    "FraudReport": "Fraud Report",
    "MandateOrAgreement": "Mandate Or Agreement",
    "ProofOfDelivery": "Proof Of Delivery",
    "Other": "Other Supporting Evidence"
  },
  "EnquiryFlow": {
    "initialStep": "concern",
    "relatedCaseInitialStep": "relation",
    "reviewStep": "review",
    "steps": [
      {
        "code": "relation",
        "kind": "relation",
        "phase": "Concern",
        "prompt": "Would you like to ask about this case or discuss something different?",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "concern",
        "kind": "concern",
        "phase": "Concern",
        "prompt": "Choose what you need help with.",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "optional-transaction",
        "kind": "optional-transaction",
        "phase": "Details",
        "prompt": "Is this about a transaction on your account?",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "transaction",
        "kind": "transaction",
        "phase": "Details",
        "prompt": "Choose the transaction you need help with.",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "case",
        "kind": "case",
        "phase": "Details",
        "prompt": "Choose the dispute you want to discuss.",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "description",
        "kind": "description",
        "phase": "Details",
        "prompt": "Tell us what happened and how we can help.",
        "completionField": "description",
        "concernFlag": null
      },
      {
        "code": "date",
        "kind": "date",
        "phase": "Details",
        "prompt": "When did this issue happen? You can skip this if you are unsure.",
        "completionField": "dateAnswered",
        "concernFlag": "collectDate"
      },
      {
        "code": "additional",
        "kind": "additional",
        "phase": "Details",
        "prompt": "Is there anything else you would like our team to know?",
        "completionField": "additionalAnswered",
        "concernFlag": null
      },
      {
        "code": "review",
        "kind": "review",
        "phase": "Review",
        "prompt": "Please check your enquiry before sending it.",
        "completionField": null,
        "concernFlag": null
      }
    ]
  },
  "UiText": {
    "common.cancel": "Cancel",
    "common.close": "Close",
    "common.reason": "Reason",
    "common.saving": "Saving…",
    "common.loading": "Loading…",
    "common.wait": "Please Wait…",
    "common.unassigned": "Unassigned",
    "dialogs.choices.search": "Search choices",
    "dialogs.conversation.close.title": "Close This Conversation?",
    "dialogs.conversation.close.description": "Closing is permanent. You can still read this conversation.",
    "dialogs.conversation.close.confirm": "Confirm Closure",
    "dialogs.conversation.assign.title": "Assign Conversation",
    "dialogs.conversation.assign.confirm": "Confirm Assignment",
    "dialogs.conversation.escalate.title": "Escalate This Conversation?",
    "dialogs.conversation.escalate.confirm": "Confirm Escalation",
    "dialogs.conversation.assignee": "Assigned Staff",
    "dialogs.evidence.remove.title": "Remove Evidence",
    "dialogs.evidence.remove.description": "This file will stop being available to view. Its protected original and removal history remain retained for the case record.",
    "dialogs.evidence.remove.placeholder": "Why are you removing this file?",
    "dialogs.evidence.remove.busy": "Removing...",
    "dialogs.evidence.remove.confirm": "Remove File",
    "dialogs.evidence.access.title": "Document Access",
    "dialogs.evidence.access.description": "Access applies to this document only. Another supervisor must approve your own access.",
    "dialogs.evidence.access.staff": "Staff Member",
    "dialogs.evidence.access.chooseStaff": "Choose Staff",
    "dialogs.evidence.access.emptyStaff": "Assign a qualified specialist to this case or ask another supervisor to review access.",
    "dialogs.evidence.access.expires": "Access Expires In",
    "dialogs.evidence.access.duration": "Access Duration",
    "dialogs.evidence.access.placeholder": "Why is access needed or being revoked?",
    "dialogs.evidence.access.confirm": "Grant Access",
    "dialogs.evidence.access.revoke": "Revoke",
    "dialogs.evidence.access.noGrants": "No staff access granted.",
    "dialogs.account.eyebrow": "YOUR ACCOUNTS",
    "dialogs.account.title": "Open Account",
    "dialogs.account.type": "Account Type",
    "dialogs.transfer.eyebrow": "SECURE TRANSFER",
    "dialogs.transfer.title": "Transfer Between My Accounts",
    "dialogs.transfer.reviewTitle": "Review Transfer",
    "dialogs.transfer.source": "From Account",
    "dialogs.transfer.destination": "To Account",
    "dialogs.transfer.selectAccount": "Select Your Account",
    "dialogs.transfer.confirm": "Confirm Transfer",
    "CaseHistoryFilters.case_status.531b1bd6": "Case Status",
    "CaseHistoryFilters.case_priority.20641b5f": "Case Priority",
    "CaseHistoryFilters.dispute_account.dabf4380": "Dispute Account",
    "CaseHistoryFilters.messages.04d7b483": "Messages",
    "CaseHistoryFilters.message_visibility.f50b1ff7": "Message Visibility",
    "CaseHistoryFilters.date_range.14d74158": "Date Range",
    "CaseHistoryFilters.from_date.61288e82": "From Date",
    "CaseHistoryFilters.to_date.8f44d1ea": "To Date",
    "CaseHistoryFilters.end_date_must_be_on_or_after_start_date.0e971206": "End date must be on or after start date.",
    "CaseHistoryFilters.reset.daee7606": "Reset",
    "CaseHistoryFilters.show.0df6f1ca": "Show",
    "AccountBalances.account_balances.a39f304a": "Account Balances",
    "AccountBalances.retry.942087cc": "Retry",
    "AccountBalances.current_balance.988a691f": "Current balance",
    "AccountBalances.available.e6744473": "Available",
    "AccountBalances.reserved.3385ffe6": "Reserved",
    "AccountBalances.main_balance.220e02e0": "Main Balance",
    "AccountBalances.active_accounts.4e2c4e6f": "Active Accounts",
    "AccountBalances.balance_history.384e4191": "Balance History",
    "AccountDeletionPanel.account_closure_requests.ec0af83e": "Account Closure Requests",
    "AccountDeletionPanel.cancel_request.397b8bd8": "Cancel Request",
    "AccountDeletionPanel.request_account_closure.378634d2": "Request Account Closure",
    "AccountDeletionPanel.enter_the_code_sent_to.3892dc93": "Enter the code sent to",
    "AccountNumberControl.account.9af21132": "account",
    "AccountNumberControl.copy_account_details.af5fc75d": "Copy Account Details",
    "AccountSwitcher.account.7e1b0d56": "Account",
    "AccountSwitcher.all_my_accounts.b88b55e9": "All my accounts",
    "AppShell.dispute_portal_overview.53a0e179": "Dispute Portal Overview",
    "AppShell.dispute_portal.0fed13ec": "Dispute Portal",
    "AppShell.more.d47d7cb0": "More",
    "AppShell.more_navigation.c5e96188": "More Navigation",
    "AppShell.workspace.87bb59ba": "Workspace",
    "AppShell.close_navigation.186d58d7": "Close Navigation",
    "AppShell.appearance.3907fa7f": "Appearance",
    "AppShell.close_appearance.b378b9e0": "Close Appearance",
    "AppShell.choose_appearance.ec6f6e43": "Choose Appearance",
    "AppShell.secure_portal.4ff5b512": "Secure portal",
    "AppShell.light_dark_or_system_appearance.5165027d": "Light, Dark Or System Appearance",
    "AppShell.profile_menu.66491b20": "Profile Menu",
    "CaseActivity.case_history.c4fdf9cd": "Case History",
    "CaseActivity.retry.942087cc": "Retry",
    "CaseFinancials.financial_details.688527b4": "Financial Details",
    "CaseFinancials.refund_reference.1bf879e3": "Refund reference",
    "CaseFinancials.copy_refund_reference.87f3a23f": "Copy Refund Reference",
    "CaseFinancials.bank_refund_reference.7a8c0c3a": "Bank refund reference",
    "CaseFinancials.copy_bank_refund_reference.05b8147e": "Copy Bank Refund Reference",
    "CaseFinancials.approve_refund.bf35c91f": "Approve Refund",
    "CaseFinancials.cancel.19766ed6": "Cancel",
    "CaseOperationsPanel.priority.3092a5f0": "priority",
    "CaseOperationsPanel.refresh_case.b52a603f": "Refresh Case",
    "CaseOperationsPanel.take_ownership.8116a0e6": "Take Ownership",
    "CaseOperationsPanel.explain_the_handover.23193437": "Explain the handover",
    "CaseOperationsPanel.severity.ce9bf63a": "Severity:",
    "CaseOperationsPanel.escalated_for_senior_review.3392d5b8": "Escalated for senior review",
    "CaseOperationsPanel.the_specialist_team_remains_responsible_until_a_handove.a89fbf0d": "The specialist team remains responsible until a handover is confirmed.",
    "CaseOperationsPanel.escalation_level.b791fbc1": "Escalation level",
    "CaseOperationsPanel.review_steps.b1fdb5d4": "Review Steps",
    "CaseOperationsPanel.cases.352b8477": "cases",
    "CaseOperationsPanel.no_staff_are_configured_for_this_team_yet_a_super_admin.787428ba": "No staff are configured for this team yet. A super admin can add qualified users.",
    "CaseOperationsPanel.eligible.947a3ddf": "eligible ·",
    "CaseOperationsPanel.unassign_this_case.b77b217f": "Unassign This Case",
    "CaseOperationsPanel.case_owner.938d4f57": "Case Owner",
    "CaseOperationsPanel.no_qualified_staff_are_available_staff_can_update_their.f8b7b37b": "No qualified staff are available. Staff can update their availability in the case management; a super admin can add team members or review capacity.",
    "CaseOperationsPanel.priority_team.1bd215ce": "Priority & Team",
    "CaseOperationsPanel.priority.d60dbba0": "Priority",
    "CaseOperationsPanel.severity.5e9f9812": "Severity",
    "CaseOperationsPanel.specialist_team.9a2fb0dd": "Specialist Team",
    "CaseOperationsPanel.save_priority.b220dc9f": "Save Priority",
    "CaseOperationsPanel.targets_run_from_submission_team_changes_return_the_cas.caa2137e": "Targets run from submission. Team changes return the case for qualified reassignment.",
    "CaseOperationsPanel.review_targets.440619c1": "Review Targets",
    "CaseOperationsPanel.the_recorded_first_response_target_is_locked.f29bc308": "The recorded first-response target is locked.",
    "CaseOperationsPanel.escalate_case.9f968afb": "Escalate Case",
    "CaseOperationsPanel.explain_why_this_case_needs_senior_attention.e6548bc5": "Explain why this case needs senior attention.",
    "CaseOperationsPanel.10_500_characters_the_customer_sees_the_escalation_your.6864853e": "10–500 characters. The customer sees the escalation; your reason stays internal.",
    "CaseOperationsPanel.request_senior_review.61cf5639": "Request Senior Review",
    "CaseProgress.case_progress.72eb8d98": "Case Progress",
    "CaseStatusCatalogue.workflow_statuses.99b5b630": "Workflow Statuses",
    "CaseStatusCatalogue.codes_and_history_are_retained_these_descriptions_do_no.4edb2bf8": "Codes and history are retained. These descriptions do not authorise case actions or establish that a refund was paid.",
    "CaseStatusCatalogue.code.340f4630": "Code",
    "CaseStatusCatalogue.display_name.18d67c99": "Display Name",
    "CaseStatusCatalogue.case_state.472c6aac": "Case State",
    "CaseStatusCatalogue.workflow_paths.20dee8a1": "Workflow Paths",
    "CaseStatusCatalogue.next_step.226366c3": "Next Step",
    "CaseStatusCatalogue.descriptions.bfdc0de7": "Descriptions",
    "CaseStatusCatalogue.update_descriptions.67df0e94": "Update Descriptions",
    "CaseStatusCatalogue.the_workflow_code_and_permitted_transitions_stay_unchan.0899f011": "The workflow code and permitted transitions stay unchanged.",
    "CaseStatusCatalogue.save_descriptions.f043c681": "Save Descriptions",
    "CaseStatusCatalogue.refresh_catalogue_and_keep_my_input.02535d78": "Refresh Catalogue And Keep My Input",
    "CaseStatusCatalogue.cancel.19766ed6": "Cancel",
    "CaseTimers.review_target.36b89017": "Review target:",
    "CaseTimers.first_response.a1977f97": "First response:",
    "CaseTimers.elapsed.a194a68a": "Elapsed",
    "CaseTimers.h_clock_includes_time_awaiting_customer_information.ae6671e7": "h · Clock includes time awaiting customer information.",
    "CountryPicker.search_countries.320113ee": "Search countries",
    "CountryPicker.no_countries_found.5bf56eb8": "No countries found.",
    "CustomerAssistanceDetails.customer_and_transaction_details.66bc1809": "Customer And Transaction Details",
    "CustomerAssistanceDetails.customer_account.b2e204f4": "Customer & Account",
    "CustomerAssistanceDetails.customer.bf376338": "Customer",
    "CustomerAssistanceDetails.email.969ccbd3": "Email",
    "CustomerAssistanceDetails.mobile.cbaad3cf": "Mobile",
    "CustomerAssistanceDetails.identity_number.4c2803ff": "Identity Number",
    "CustomerAssistanceDetails.date_of_birth.90952e89": "Date Of Birth",
    "CustomerAssistanceDetails.address.56ef8f20": "Address",
    "CustomerAssistanceDetails.account.7e1b0d56": "Account",
    "CustomerAssistanceDetails.account_number.a4783e73": "Account Number",
    "CustomerAssistanceDetails.copy_account_number.348c97d7": "Copy Account Number",
    "CustomerAssistanceDetails.customer_registered.76baf6d3": "Customer Registered",
    "CustomerAssistanceDetails.transaction.eec26ddd": "Transaction",
    "CustomerAssistanceDetails.merchant.1da9a042": "Merchant",
    "CustomerAssistanceDetails.amount.49e96d7c": "Amount",
    "CustomerAssistanceDetails.date_time.a217562d": "Date & Time",
    "CustomerAssistanceDetails.type_status.34b5de48": "Type & Status",
    "CustomerAssistanceDetails.description.526e0087": "Description",
    "CustomerAssistanceDetails.bank_reference.411dfe98": "Bank Reference",
    "CustomerAssistanceDetails.copy_transaction_reference.d18fddcc": "Copy Transaction Reference",
    "CustomerAssistanceDetails.customer_refund_reference.04327db0": "Customer Refund Reference",
    "CustomerAssistanceDetails.copy_refund_reference.87f3a23f": "Copy Refund Reference",
    "CustomerAssistanceDetails.transaction_id.be8c6227": "Transaction ID",
    "CustomerAssistanceDetails.copy_internal_transaction_id.73f9baf7": "Copy Internal Transaction ID",
    "DisputeSecurityCheck.request_specialist_review.fff72a64": "Request Specialist Review",
    "DisputeSecurityCheck.start_again.0ec60501": "Start Again",
    "DisputeSecurityCheck.a_specialist_will_investigate_your_request_a_refund_is.bc0a3a79": "A specialist will investigate your request. A refund is subject to review.",
    "DisputeSubmissionForm.loading_dispute_options.69196931": "Loading Dispute Options…",
    "DisputeSubmissionForm.dispute_reason.841320f0": "Dispute Reason",
    "DisputeSubmissionForm.add_a_few_details.c941a375": "Add A Few Details",
    "DisputeSubmissionForm.share_anything_that_might_help_us_understand_what_happe.640e8156": "Share anything that might help us understand what happened.",
    "DisputeSubmissionForm.keep_it_clear_and_avoid_sharing_passwords_or_full_card.668664d2": "Keep it clear and avoid sharing passwords or full card details.",
    "DisputeSubmissionForm.check_everything_looks_right.c236d93b": "Check Everything Looks Right",
    "DisputeSubmissionForm.we_ll_use_these_details_to_start_your_review.9b398e78": "We’ll use these details to start your review.",
    "DisputeSubmissionForm.transaction.8dadb567": "TRANSACTION",
    "DisputeSubmissionForm.disputed_amount.79ca731d": "DISPUTED AMOUNT",
    "DisputeSubmissionForm.your_concern.0a02c5d4": "YOUR CONCERN",
    "DisputeSubmissionForm.reconnect_to_securely_submit_your_dispute.e7e4a665": "Reconnect to securely submit your dispute.",
    "EvidenceAccessPanel.approved_by.8e838c46": "Approved by",
    "EvidencePanel.case_evidence.34e32e18": "Case Evidence",
    "EvidencePanel.evidence.03867aea": "Evidence",
    "EvidencePanel.refresh.0e916101": "Refresh",
    "EvidencePanel.document_category.f57093e9": "Document Category",
    "EvidencePanel.choose_evidence_files.dcb739d7": "Choose Evidence Files",
    "EvidencePanel.take_evidence_photo.04b74879": "Take Evidence Photo",
    "EvidencePanel.pdf_jpeg_png_or_webp_optimised_images_up_to_5_mb_origin.a8cda4b6": "PDF, JPEG, PNG or WebP · optimised images up to 5 MB. Original photos retained; 25 MB total per case.",
    "EvidencePanel.selected_evidence_preview.236e7797": "Selected evidence preview",
    "EvidencePanel.optimised_copy_original_retained.761862f8": "Optimised copy · original retained (",
    "EvidencePanel.preparing_photo.e3705a9e": "Preparing photo…",
    "EvidencePanel.sending.e595f17f": "Sending",
    "EvidencePanel.of.28391d3b": "of",
    "EvidencePanel.files_saved.94803087": "files saved",
    "EvidencePanel.loading_evidence.cad42e39": "Loading evidence…",
    "EvidencePanel.removed_by.871368c7": "Removed By",
    "EvidencePanel.no_evidence_attached_yet.3ebbaca0": "No evidence attached yet.",
    "EvidencePreview.document_preview.071b2af3": "Document Preview",
    "EvidencePreview.close_document_preview.6a29fbf7": "Close Document Preview",
    "EvidencePreview.preparing_preview.11e66e1b": "Preparing preview…",
    "EvidencePreview.document_pages.5166ea94": "Document Pages",
    "EvidencePreview.previous_page.a163aaa2": "Previous Page",
    "EvidencePreview.next_page.6ead5dfd": "Next Page",
    "EvidencePreview.download_retained_original.451e6391": "Download Retained Original",
    "GuidedEnquiry.guided_enquiry.5304bd28": "Guided Enquiry",
    "GuidedEnquiry.back_to_messages.70db8f70": "Back To Messages",
    "GuidedEnquiry.dispute_portal_support.0600456c": "Dispute Portal Support",
    "GuidedEnquiry.new_enquiry.463829ea": "New Enquiry",
    "GuidedEnquiry.draft.d00cf49b": "Draft ·",
    "GuidedEnquiry.you_are_offline_your_draft_stays_here.0fae558b": "You Are Offline. Your Draft Stays Here.",
    "GuidedEnquiry.enquiry_conversation.5730bfd3": "Enquiry Conversation",
    "GuidedEnquiry.hi.3639efcd": "Hi",
    "GuidedEnquiry._how_can_we_help_you_today.1bc429f0": "! How can we help you today?",
    "GuidedEnquiry.opening_your_enquiry.7f579f3e": "Opening your enquiry…",
    "GuidedEnquiry.change_case.4f245fcf": "Change Case",
    "GuidedEnquiry.change_transaction.4027ffa2": "Change Transaction",
    "GuidedEnquiry.tell_us_what_happened_and_how_we_can_help.94d3534e": "Tell us what happened and how we can help.",
    "GuidedEnquiry.edit_details.8cfa48f3": "Edit Details",
    "GuidedEnquiry.is_there_anything_else_you_would_like_our_team_to_know.48f56802": "Is there anything else you would like our team to know?",
    "GuidedEnquiry.concern_options.634530c0": "Concern Options",
    "GuidedEnquiry.search.49c266ba": "Search",
    "GuidedEnquiry.loading.ba3bbbe1": "Loading…",
    "GuidedEnquiry.no_matching_transactions_try_another_search.f5c42ea1": "No Matching Transactions. Try Another Search.",
    "GuidedEnquiry.no_matching_cases_try_another_reference.b0f87597": "No Matching Cases. Try Another Reference.",
    "GuidedEnquiry.load_more.3f06cd33": "Load More",
    "GuidedEnquiry.back_to_concerns.1ccd1bb8": "Back To Concerns",
    "GuidedEnquiry.continue.31fbef16": "Continue",
    "GuidedEnquiry.i_am_not_sure.e76f148c": "I Am Not Sure",
    "GuidedEnquiry.review_your_enquiry.e633d328": "Review Your Enquiry",
    "GuidedEnquiry.your_enquiry.2435405c": "Your Enquiry",
    "GuidedEnquiry.concern.bead1be3": "Concern",
    "GuidedEnquiry.related_case.e71ce067": "Related Case",
    "GuidedEnquiry.transaction.eec26ddd": "Transaction",
    "GuidedEnquiry.description.526e0087": "Description",
    "GuidedEnquiry.additional_details.88dd4af6": "Additional Details",
    "GuidedEnquiry.when_it_happened.d1534261": "When It Happened",
    "GuidedEnquiry.only_submit_when_these_details_are_correct.c39a6c7f": "Only Submit When These Details Are Correct.",
    "GuidedEnquiry.retry.942087cc": "Retry",
    "GuidedEnquiry.your_message.b5be5d4e": "Your Message",
    "GuidedEnquiry.never_share_passwords_pins_or_full_card_numbers.6f302abe": "Never Share Passwords, PINs Or Full Card Numbers.",
    "GuidedEnquiry.nothing_else.3a555132": "Nothing Else",
    "GuidedEnquiry.your_enquiry_is_sent_only_after_you_confirm.829ee18d": "Your Enquiry Is Sent Only After You Confirm.",
    "IdentityNumberControl.locked.a424e33d": "Locked",
    "NotificationPreferences.push_notifications.3b1776f5": "Push Notifications",
    "NotificationPreferences.in_app.c5643d7d": "In-App",
    "NotificationPreferences.email.969ccbd3": "Email",
    "NotificationPreferences.sms.40b601a1": "SMS",
    "NotificationPreferences.whatsapp.6a40edf1": "WhatsApp",
    "NotificationPreferences.save_preferences.e5b8e690": "Save Preferences",
    "PasswordChangePanel.change_password.a949f462": "Change Password",
    "PasswordChangePanel.changing_your_password_signs_out_every_device.912d692d": "Changing your password signs out every device.",
    "PasswordChangePanel.current_password.8ac76099": "Current Password",
    "PasswordChangePanel.new_password.7c451e0f": "New Password",
    "PasswordChangePanel.confirm_new_password.642776c0": "Confirm New Password",
    "PolicyVersionAdministration.published_settings_are_retained_a_replacement_never_rev.5f25e56b": "Published settings are retained. A replacement never revives an older version when it expires or is removed.",
    "PolicyVersionAdministration.create_new_version.69707da7": "Create New Version",
    "PolicyVersionAdministration.as_a_new_version.a3e775db": "As A New Version",
    "PolicyVersionAdministration.enabled.92c1cdfd": "Enabled",
    "PolicyVersionAdministration.disabled.75081b59": "Disabled",
    "PolicyVersionAdministration.leave_empty_to_publish_immediately.9c3cc73d": "Leave Empty To Publish Immediately",
    "PolicyVersionAdministration.optional.59be7133": "Optional",
    "PolicyVersionAdministration.use_latest_concurrency_token_and_keep_my_input.f05f65aa": "Use Latest Concurrency Token And Keep My Input",
    "PolicyVersionAdministration.preview_new_version.e984584b": "Preview New Version",
    "PolicyVersionAdministration.before_and_after.706bf7e0": "Before And After",
    "PolicyVersionAdministration.setting.818cd8d9": "Setting",
    "PolicyVersionAdministration.currently_effective.b62f5d5d": "Currently Effective",
    "PolicyVersionAdministration.new_version.6402faa1": "New Version",
    "PolicyVersionAdministration.feature_availability.4a77b49a": "Feature Availability",
    "PolicyVersionAdministration.priority.d60dbba0": "Priority",
    "PolicyVersionAdministration.start.e4bb9f1e": "Start",
    "PolicyVersionAdministration.expiry.6956d814": "Expiry",
    "PolicyVersionAdministration.at_the_new_start.6f1d7919": "At The New Start:",
    "PolicyVersionAdministration.publish_new_version.109e6450": "Publish New Version",
    "PolicyVersionAdministration.cancel.19766ed6": "Cancel",
    "PolicyVersionAdministration.version_or_change_reason.c34aaea3": "Version Or Change Reason",
    "PolicyVersionAdministration.search_history.66d7e4a1": "Search History",
    "PolicyVersionAdministration.version.dd167905": "Version",
    "PolicyVersionAdministration.lifecycle.46459b1f": "Lifecycle",
    "PolicyVersionAdministration.feature.3d377ae9": "Feature",
    "PolicyVersionAdministration.published_by.5c782f11": "Published By",
    "PolicyVersionAdministration.actions.ff8059dc": "Actions",
    "PolicyVersionAdministration.view_details.90789c12": "View Details",
    "PolicyVersionAdministration.as_new_version.c8bf30e7": "As New Version",
    "PolicyVersionAdministration.remove_version.eb17f670": "Remove Version",
    "PolicyVersionAdministration.previous.a57b08a4": "Previous",
    "PolicyVersionAdministration.versions.f89ea270": "Versions",
    "PolicyVersionAdministration.next.1ff57a29": "Next",
    "PolicyVersionAdministration._read_only.c5a31570": "· Read Only",
    "PolicyVersionAdministration.removed.4118fb4f": "Removed",
    "PolicyVersionAdministration.by.125466b8": "By",
    "PolicyVersionAdministration.close_details.d33f48d7": "Close Details",
    "PolicyVersionAdministration.the_content_and_history_remain_retained_older_versions.77166ce2": "The content and history remain retained. Older versions will not resume.",
    "PolicyVersionAdministration.preview_removal.8efe4a13": "Preview Removal",
    "PolicyVersionAdministration.at.c72c5404": "At",
    "PolicyVersionAdministration._removing_a_scheduled_replacement_does_not_reinstate_ol.41001858": ". Removing A Scheduled Replacement Does Not Reinstate Older Versions.",
    "PolicyVersionAdministration.confirm_soft_deletion.0f0d7668": "Confirm Soft Deletion",
    "RememberedDevices.two_step_verification.6b19ad6a": "Two-Step Verification",
    "RememberedDevices.loading_remembered_devices.f793379f": "Loading remembered devices…",
    "RememberedDevices.remembered_sign_ins_will_be_removed.2d39f337": "Remembered sign-ins will be removed.",
    "RememberedDevices.cancel.19766ed6": "Cancel",
    "RememberedDevices.a_code_is_required_each_time_you_sign_in.e11ef02a": "A code is required each time you sign in.",
    "RememberedDevices.your_password_is_always_required_remove_a_remembered_si.2cd1d91e": "Your password is always required. Remove a remembered sign-in to require a code next time.",
    "RememberedDevices.remembered_devices.e88eeb81": "Remembered Devices",
    "RememberedDevices.no_remembered_devices.cc1a873a": "No remembered devices.",
    "RememberedDevices.last_used.830ec7f8": "Last used",
    "RememberedDevices.expires.f6725f3a": "Expires",
    "SecurityActivity.last_5_sign_ins.2dab89a2": "Last 5 Sign-Ins",
    "SecurityActivity.select_a_sign_in_to_review_its_details.b673ac18": "Select a sign-in to review its details.",
    "SecurityActivity.refresh.0e916101": "Refresh",
    "SecurityActivity.loading_sign_ins.1cb98ca9": "Loading sign-ins…",
    "SecurityActivity.your_next_successful_sign_in_will_appear_here.70f4359f": "Your next successful sign-in will appear here.",
    "SecurityActivity.sign_in_details.9f4b8895": "Sign-In Details",
    "SecurityActivity.close_sign_in_details.7fed3abe": "Close Sign-In Details",
    "SecurityActivity.loading_security_details.602b43d9": "Loading security details…",
    "SecurityActivity.signed_in.f44ee573": "Signed In",
    "SecurityActivity.device.6ba0bdec": "Device",
    "SecurityActivity.browser.d31de1a5": "Browser",
    "SecurityActivity.ip_address.3c3de0c9": "IP Address",
    "SecurityActivity.location.15b61974": "Location",
    "SecurityActivity.session.6959b415": "Session",
    "SecurityActivity.browser_information.82881601": "Browser Information",
    "SecurityActivity.event_reference.e3197ff2": "Event Reference",
    "SecurityActivity.utc_timestamp.a0c55a35": "UTC Timestamp",
    "SecurityActivity.device_details_are_reported_by_the_browser_location_is.009af901": "Device details are reported by the browser. Location is shown only when available.",
    "SettingsComparison.no_business_values_change_publication_still_creates_a_n.98f198b5": "No Business Values Change. Publication Still Creates A New Version.",
    "SettingsComparison.setting.818cd8d9": "Setting",
    "SettingsComparison.before.9bb72500": "Before",
    "SettingsVersionHistory.configuration_version_history.30fee0ef": "Configuration Version History",
    "SettingsVersionHistory.country_registration_account_and_financial_settings_are.243cb58e": "Country, registration, account and financial settings are retained as complete snapshots. Country changes remain immediate and recheck financial eligibility; they cannot be scheduled around that safeguard.",
    "SettingsVersionHistory.search_versions.58557982": "Search Versions",
    "SettingsVersionHistory.version.dd167905": "Version",
    "SettingsVersionHistory.lifecycle.46459b1f": "Lifecycle",
    "SettingsVersionHistory.country.701d021d": "Country",
    "SettingsVersionHistory.recorded_at.8ee903fa": "Recorded At",
    "SettingsVersionHistory.actions.ff8059dc": "Actions",
    "SettingsVersionHistory.read_details.40ff5f57": "Read Details",
    "SettingsVersionHistory.as_new_version.c8bf30e7": "As New Version",
    "SettingsVersionHistory.remove_version.eb17f670": "Remove Version",
    "SettingsVersionHistory.previous.a57b08a4": "Previous",
    "SettingsVersionHistory.versions.f89ea270": "Versions",
    "SettingsVersionHistory.next.1ff57a29": "Next",
    "SettingsVersionHistory._read_only.c5a31570": "· Read Only",
    "SettingsVersionHistory.published_by.4ea64915": "Published By:",
    "SettingsVersionHistory.account_types.9422f469": "Account Types",
    "SettingsVersionHistory.maximum_accounts.1568da1b": "Maximum Accounts",
    "SettingsVersionHistory.identity_verification.11c4c9be": "Identity Verification",
    "SettingsVersionHistory.removed.4118fb4f": "Removed",
    "SettingsVersionHistory._removed_by.68bc0a0e": "· Removed By:",
    "SettingsVersionHistory.refresh_history_and_keep_my_input.eb618b34": "Refresh History And Keep My Input",
    "SettingsVersionHistory.preview_change.431ebce7": "Preview Change",
    "SettingsVersionHistory.before.c19f7558": "Before:",
    "SettingsVersionHistory._after_version.672acc32": ". After: Version",
    "SettingsVersionHistory._the_retained_source_is_copied_all_financial_safeguards.6e1c54ac": ". The retained source is copied; all financial safeguards are checked again.",
    "SettingsVersionHistory.publish_new_version.109e6450": "Publish New Version",
    "SettingsVersionHistory.confirm_soft_deletion.0f0d7668": "Confirm Soft Deletion",
    "SettingsVersionHistory.close_details.d33f48d7": "Close Details",
    "SupportConversation.conversation_details.b6b6f1ff": "Conversation Details",
    "SupportConversation.back_to_messages.70db8f70": "Back To Messages",
    "SupportConversation.support.be91940b": "Support",
    "SupportConversation.open_full_screen.92172a1f": "Open Full Screen",
    "SupportConversation.conversation_actions.cca1583b": "Conversation Actions",
    "SupportConversation.linked_records.35ff34cf": "Linked Records",
    "SupportConversation.open_linked_transaction.f571ba0f": "Open Linked Transaction",
    "SupportConversation.open_transaction.1c19e2c9": "Open Transaction",
    "SupportConversation.open_linked_case.ffe56f3e": "Open Linked Case",
    "SupportConversation.open_case.8248b503": "Open Case",
    "SupportConversation.case_conversation_history.550299db": "Case Conversation History",
    "SupportConversation.case_history.c4fdf9cd": "Case History",
    "SupportConversation.assign_conversation.dcc6d630": "Assign Conversation",
    "SupportConversation.close_conversation.293c79be": "Close Conversation",
    "SupportConversation.escalate_conversation.b527c052": "Escalate Conversation",
    "SupportConversation.offline_your_draft_stays_here.dc19382f": "Offline · your draft stays here.",
    "SupportConversation.conversation_messages.43a42836": "Conversation Messages",
    "SupportConversation.back_to_latest.823c19dd": "Back To Latest",
    "SupportConversation.new_messages.e168e17a": "New Messages",
    "SupportConversation.enquiry_details.913a399d": "Enquiry Details",
    "SupportConversation.concern.bead1be3": "Concern",
    "SupportConversation.when_it_happened.d1534261": "When It Happened",
    "SupportConversation._sent.89e62523": "· Sent",
    "SupportConversation.loading_conversation.41fa6534": "Loading Conversation",
    "SupportConversation.start_new_conversation.9518d1a2": "Start New Conversation",
    "SupportConversation.your_message.b5be5d4e": "Your Message",
    "SupportConversation.write_a_message.dc9f3f9f": "Write a message…",
    "SupportConversation.never_share_passwords_pins_or_verification_codes.e06a9d30": "Never share passwords, PINs or verification codes.",
    "ToastHost.notifications.78801183": "Notifications",
    "ToastHost.dismiss_notification.2bc163fb": "Dismiss Notification",
    "TransactionList.loading_transactions.e578cfc2": "Loading Transactions",
    "TransactionList.no_transactions_found.f03d0a03": "No transactions found",
    "TransactionList.try_changing_your_filters_or_check_back_later.46b474ea": "Try changing your filters or check back later.",
    "AccessRecoveryView.dispute_portal.0fed13ec": "Dispute Portal",
    "AccessRecoveryView.secure_from_the_start.36ddd4a1": "SECURE FROM THE START",
    "AccessRecoveryView.back_to_sign_in.ef5889ce": "Back To Sign-In",
    "AccessRecoveryView.try_again.df0fe9e0": "Try Again",
    "AccessRecoveryView.first_name.96ce6e61": "First Name",
    "AccessRecoveryView.surname.f762da05": "Surname",
    "AccessRecoveryView.verify_using.abad69fa": "Verify Using",
    "AccessRecoveryView.1_request_received.20331f83": "1 · Request received",
    "AccessRecoveryView.2_verify_ownership.d6956ccb": "2 · Verify ownership",
    "AccessRecoveryView.codes_expire_after_5_minutes_if_yours_does_not_arrive_c.89a35d3f": "Codes expire after 5 minutes. If yours does not arrive, choose Start again and try another verified channel.",
    "AccessRecoveryView.six_digit_verification_code.ae4a65c7": "Six-Digit Verification Code",
    "AccessRecoveryView.confirm_password.c292210c": "Confirm Password",
    "AccessRecoveryView.at_least_12_characters_with_upper_and_lower_case_letter.3cdbadc4": "At least 12 characters, with upper and lower case letters, a number and a symbol.",
    "AccessRecoveryView.start_again.0ec60501": "Start Again",
    "AccessRecoveryView.your_contact_must_be_verified_before_access_is_granted.85929450": "Your contact must be verified before access is granted. We never send passwords in messages.",
    "AccountDetailView.back_to_overview.32a60fdb": "Back To Overview",
    "AccountDetailView.opened.b19fb8d1": "Opened",
    "AccountDetailView.last_refreshed.c04c38e8": "Last Refreshed",
    "AccountDetailView.transactions.3e5136fd": "Transactions",
    "AccountDetailView.choose_a_transaction_to_dispute.6573d077": "Choose A Transaction To Dispute",
    "AccountDetailView.disputes.110fa2bb": "Disputes",
    "AccountDetailView.cases_linked_to_this_account.ee1607d1": "Cases Linked To This Account",
    "AccountDetailView.account_services.14a8e47b": "Account Services",
    "AccountRequestsView.account_closure_requests.ec0af83e": "Account Closure Requests",
    "AccountRequestsView.request_status.c4760f9f": "Request Status",
    "AccountRequestsView.no_requests_here.cb41308f": "No requests here.",
    "AccountRequestsView.next_step.226366c3": "Next Step",
    "AccountRequestsView.completing_closure_closes_access_signs_out_every_device.7dc99f07": "Completing closure closes access, signs out every device and removes personal profile details. Open disputes must be resolved first.",
    "AdminAuditView.audit_history.57563ca1": "Audit History",
    "AdminAuditView.account_and_case_activity.3a53c569": "Account and case activity.",
    "AdminAuditView.retry.942087cc": "Retry",
    "AdminAuditView.activity_type.f7b447cd": "Activity Type",
    "AdminAuditView.loading_activity.75c275bd": "Loading Activity",
    "AdminAuditView.view_details.90789c12": "View Details",
    "AdminAuditView.record.bfdd5106": "Record",
    "AdminAuditView.request_correlation.69448db4": "Request Correlation",
    "AdminAuditView.no_activity_found.7536b283": "No activity found",
    "AdminAuditView.choose_another_activity_type.d0745f75": "Choose another activity type.",
    "AdminAuditView.page.0a30a815": "Page",
    "AdminAuditView.of.28391d3b": "of",
    "AdminAuditView.previous.a57b08a4": "Previous",
    "AdminAuditView.next.1ff57a29": "Next",
    "AdminDisputeView.back_to_case_management.1221145d": "Back To Case Management",
    "AdminDisputeView.unassign.6ca6a19e": "Unassign",
    "AdminDisputeView.case_workspace.b9bf84c7": "Case Workspace",
    "AdminDisputeView.overview.d4b1ea57": "Overview",
    "AdminDisputeView.workflow.2e2d5c56": "Workflow",
    "AdminDisputeView.activity.38da1505": "Activity",
    "AdminDisputeView.evidence.03867aea": "Evidence",
    "AdminDisputeView.customer_transaction.7440ebb0": "CUSTOMER & TRANSACTION",
    "AdminDisputeView.case_overview.64f69765": "Case Overview",
    "AdminDisputeView.priority.d60dbba0": "Priority",
    "AdminDisputeView.customer_account.b2e204f4": "Customer & Account",
    "AdminDisputeView.customer.8df6bc3f": "CUSTOMER",
    "AdminDisputeView.transaction.8dadb567": "TRANSACTION",
    "AdminDisputeView.amount.12750056": "AMOUNT",
    "AdminDisputeView.case_history.7b3a0638": "CASE HISTORY",
    "AdminDisputeView.status_and_notes.e1b1a54b": "Status And Notes",
    "AdminDisputeView.reconnect_before_saving_a_case_update.1116120c": "Reconnect before saving a case update.",
    "AdminDisputeView.next_status.38e88229": "Next Status",
    "AdminDisputeView.keep_this_clear_and_appropriate_for_the_customer.9a4b7c14": "Keep this clear and appropriate for the customer.",
    "AdminDisputeView.case_notes.025d3934": "CASE NOTES",
    "AdminDisputeView.add_a_note.1411b667": "Add A Note",
    "AdminDisputeView.reconnect_before_adding_a_note.d958beba": "Reconnect before adding a note.",
    "AdminDisputeView.who_can_see_this.03005c1b": "Who Can See This?",
    "AdminDisputeView.internal_staff_only.49c007f8": "Internal staff only",
    "AdminDisputeView.customer_visible_update.8ab86602": "Customer-visible update",
    "AdminDisputeView.message.2f77668a": "Message",
    "AdminDisputeView.add_a_clear_relevant_note.ffdd34b0": "Add a clear, relevant note…",
    "AdminDisputeView.read_only_access.b4214ac4": "Read-only access",
    "AdminDisputeView.case_unavailable.68120e83": "Case Unavailable",
    "AdminQueueView.case_management.75377baf": "Case Management",
    "AdminQueueView.active_cases_and_completed_history.867fddec": "Active cases and completed history.",
    "AdminQueueView.retry.942087cc": "Retry",
    "AdminQueueView.retry_workload_details.1a878d0b": "Retry Workload Details",
    "AdminQueueView.your_workload.514429cb": "Your workload",
    "AdminQueueView.active_cases.2d6d373a": "active cases",
    "AdminQueueView.your_availability.06f441ca": "Your Availability",
    "AdminQueueView.case_overview.64f69765": "Case Overview",
    "AdminQueueView.closed_cases.66d06014": "Closed Cases",
    "AdminQueueView.cases.2249bd50": "Cases",
    "AdminQueueView.case_status.531b1bd6": "Case Status",
    "AdminQueueView.search_disputes.9d0d5cea": "Search Disputes",
    "AdminQueueView.reference_customer_or_merchant.c83108b2": "Reference, customer or merchant",
    "AdminQueueView.search_cases.da8b5da5": "Search Cases",
    "AdminQueueView.loading_cases.1614070b": "Loading Cases",
    "AdminQueueView.priority.c893795e": "Priority ·",
    "AdminQueueView.severity.5e9f9812": "Severity",
    "AdminQueueView.no_matching_cases.51ee18cb": "No matching cases",
    "AdminQueueView.clear_filters.5776503c": "Clear Filters",
    "AdminQueueView.page.0a30a815": "Page",
    "AdminQueueView.of.28391d3b": "of",
    "AdminQueueView.previous.a57b08a4": "Previous",
    "AdminQueueView.next.1ff57a29": "Next",
    "DashboardView.your_accounts.1af2cf78": "Your Accounts",
    "DashboardView.accounts.8a7c8b67": "Accounts",
    "DashboardView.linked.2272bea6": "linked",
    "DashboardView.open.ed077f3d": "Open",
    "DashboardView.your_activity.e5ef6da1": "YOUR ACTIVITY",
    "DashboardView.recent_transactions.d8d9b7dd": "Recent Transactions",
    "DashboardView.case_updates.0a811bea": "CASE UPDATES",
    "DashboardView.dispute_tracker.09ef168a": "Dispute Tracker",
    "DashboardView.all_clear_for_now.4fdbe1a9": "All clear for now",
    "DashboardView.your_submitted_disputes_and_updates_will_appear_here.bd5d6552": "Your submitted disputes and updates will appear here.",
    "DisputeDetailView.back_to_disputes.9cb17dee": "Back To Disputes",
    "DisputeDetailView.loading_case.20ff6c7a": "Loading Case",
    "DisputeDetailView.case_unavailable.68120e83": "Case Unavailable",
    "DisputeDetailView.try_again.df0fe9e0": "Try Again",
    "DisputeDetailView.disputed_amount.51b79f63": "Disputed amount",
    "DisputeDetailView.your_reply_is_needed.da884579": "Your reply is needed",
    "DisputeDetailView.case_sections.72c10395": "Case Sections",
    "DisputeDetailView.progress.4664827f": "Progress",
    "DisputeDetailView.details.45989de4": "Details",
    "DisputeDetailView.evidence.03867aea": "Evidence",
    "DisputeDetailView.transaction_dispute.04ac3de0": "Transaction & Dispute",
    "DisputeDetailView.transaction_type.e9c992fe": "Transaction Type",
    "DisputeDetailView.description.526e0087": "Description",
    "DisputeDetailView.transaction_date.b3cf57b8": "Transaction Date",
    "DisputeDetailView.account.7e1b0d56": "Account",
    "DisputeDetailView.reason.f81ab834": "Reason",
    "DisputeDetailView.review_team.338652f6": "Review Team",
    "DisputeDetailView.withdraw_dispute.b2794868": "Withdraw Dispute",
    "DisputeDetailView.withdraw_this_dispute.048be00f": "Withdraw This Dispute?",
    "DisputeDetailView.this_ends_the_review_and_closes_case_messages_your_disp.5cfbc57a": "This ends the review and closes case messages. Your dispute and evidence remain in your case history.",
    "DisputeDetailView.keep_dispute_open.44d34dcc": "Keep Dispute Open",
    "DisputeDetailView.no_longer_need_this_review.cbde2115": "No Longer Need This Review?",
    "DisputeDetailView.you_can_withdraw_your_dispute_while_it_is_open.9581ecf0": "You can withdraw your dispute while it is open.",
    "DisputeRulesView.policy.c611981f": "Policy",
    "DisputeRulesView.review_effective_settings_and_publish_new_versions.e303ec91": "Review Effective Settings And Publish New Versions.",
    "DisputesView.my_disputes.c988b30a": "My Disputes",
    "DisputesView.search_disputes.9d0d5cea": "Search Disputes",
    "DisputesView.search_disputes.81e7dbc8": "Search disputes",
    "DisputesView.clear_case_search.46325dd0": "Clear Case Search",
    "DisputesView.find_disputes.24d949a2": "Find Disputes",
    "DisputesView.cases.352b8477": "cases",
    "DisputesView.retry.942087cc": "Retry",
    "DisputesView.loading_disputes.59e271fc": "Loading Disputes",
    "DisputesView.reply_requested.9b8bb0f5": "Reply Requested",
    "DisputesView.previous.a57b08a4": "Previous",
    "DisputesView.next.1ff57a29": "Next",
    "HomeEntry.opening_your_workspace.bed9096c": "Opening your workspace…",
    "HomeView.a_previous_transfer_is_awaiting_a_confirmed_response.01f399ab": "A previous transfer is awaiting a confirmed response.",
    "HomeView.check_or_cancel_request.8b73d77d": "Check Or Cancel Request",
    "HomeView.your_accounts.1af2cf78": "Your Accounts",
    "HomeView.choose_account.3ae698de": "Choose Account",
    "HomeView.open_account_details.a4e4ceea": "Open Account Details",
    "HomeView.available_balance.e88b0aee": "Available Balance",
    "HomeView.current_balance.f53e5612": "Current Balance",
    "HomeView.reserved_funds.a98fe13d": "Reserved Funds",
    "HomeView.disputes.110fa2bb": "Disputes",
    "MonitoringView.service_performance.eacae804": "SERVICE PERFORMANCE",
    "MonitoringView.api_monitoring.64032b7a": "API Monitoring",
    "MonitoringView.stored_request_statistics_from_the_portal_s_database.bada2b0c": "Stored request statistics from the portal''s database.",
    "MonitoringView.refresh_monitoring.d0728e94": "Refresh Monitoring",
    "MonitoringView.last_hour.fb4408ad": "Last Hour",
    "MonitoringView.last_24_hours.615c5daf": "Last 24 Hours",
    "MonitoringView.last_7_days.b84372e5": "Last 7 Days",
    "MonitoringView.search_routes_or_methods.a1b2593f": "Search Routes Or Methods",
    "MonitoringView.search_monitoring.e955682b": "Search Monitoring",
    "MonitoringView.all_statuses.05f56712": "All Statuses",
    "MonitoringView.successful.f3686bb3": "Successful",
    "MonitoringView.redirects.4340f285": "Redirects",
    "MonitoringView.client_errors.2b2864a5": "Client Errors",
    "MonitoringView.server_errors.844cf786": "Server Errors",
    "MonitoringView.ms.f785c3ce": "ms",
    "MonitoringView.endpoint_performance.fe8da20d": "Endpoint Performance",
    "MonitoringView.up_to_200_endpoint_groups_http_4xx_are_counted_separate.26de221e": "Up to 200 endpoint groups. HTTP 4xx are counted separately from server failures.",
    "MonitoringView.loading_stored_metrics.db8779a8": "Loading Stored Metrics…",
    "MonitoringView.endpoint.3df9726c": "Endpoint",
    "MonitoringView.status.920e413c": "Status",
    "MonitoringView.requests.ada27592": "Requests",
    "MonitoringView.failures.7bd31a02": "Failures",
    "MonitoringView.average.b38ae2c5": "Average",
    "MonitoringView.maximum.66c9ab17": "Maximum",
    "MonitoringView.no_matching_metrics.e6f1b64c": "No Matching Metrics",
    "MonitoringView.completed_requests_appear_after_the_next_aggregation_fl.cf0a3021": "Completed requests appear after the next aggregation flush.",
    "MonitoringView.last_saved.5c7e4a1e": "Last Saved:",
    "MonitoringView._buffered_groups.c2330c67": "· Buffered Groups:",
    "MonitoringView._dropped_measurements.7264082a": "· Dropped Measurements:",
    "NewDisputeView.back_to_transaction.ef5f0810": "Back To Transaction",
    "NewDisputeView.transaction_unavailable.f8c5a93b": "Transaction Unavailable",
    "NewDisputeView.dispute_received.c07df35f": "Dispute Received",
    "NewDisputeView.add_evidence_below.02035140": "Add evidence below.",
    "NewDisputeView.your_dispute_reference.a2ee2184": "Your dispute reference",
    "NewDisputeView.dispute_transaction.e2aaac04": "Dispute Transaction",
    "NotificationsView.notifications.78801183": "Notifications",
    "NotificationsView.your_notifications.92eb2431": "Your Notifications",
    "NotificationsView.try_again.df0fe9e0": "Try Again",
    "NotificationsView.loading_updates.079dcad7": "Loading updates…",
    "NotificationsView.newest_first.ffb6f576": "Newest first",
    "NotificationsView.unread.1b9f384c": "Unread",
    "NotificationsView.you_re_up_to_date.43c0d54c": "You’Re Up To Date",
    "NotificationsView.case_updates_will_appear_here.b1d6c038": "Case updates will appear here.",
    "NotificationsView.notification_pages.b1f7505d": "Notification Pages",
    "NotificationsView.page.0a30a815": "Page",
    "NotificationsView.of.28391d3b": "of",
    "NotificationsView.previous.a57b08a4": "Previous",
    "NotificationsView.next.1ff57a29": "Next",
    "PlatformAdministrationView.dispute_administration.162b7ba1": "DISPUTE ADMINISTRATION",
    "PlatformAdministrationView.dispute_operations.8c1f89b3": "Dispute Operations",
    "PlatformAdministrationView.manage_refund_reviews_reconciliation_and_country_polici.43b94fb9": "Manage refund reviews, reconciliation and country policies.",
    "PlatformAdministrationView.refresh_dispute_operations.146556e1": "Refresh Dispute Operations",
    "PlatformAdministrationView.search_dispute_operations.9c81805d": "Search Dispute Operations",
    "PlatformAdministrationView.filter_review_status.834f1872": "Filter Review Status",
    "PlatformAdministrationView.all_statuses.05f56712": "All Statuses",
    "PlatformAdministrationView.filter_currency.c21efe7c": "Filter Currency",
    "PlatformAdministrationView.all_currencies.4ad57547": "All Currencies",
    "PlatformAdministrationView.schedule_a_refund_review.c2f321fb": "Schedule A Refund Review",
    "PlatformAdministrationView.select_a_confirmed_transaction_customer_account_and_amo.a657c17b": "Select a confirmed transaction. Customer, account and amount details are captured automatically.",
    "PlatformAdministrationView.customer_merchant_or_reference.ff4ffb09": "Customer, Merchant Or Reference",
    "PlatformAdministrationView.find_original_transaction.5f9291c0": "Find Original Transaction",
    "PlatformAdministrationView.no_matching_transactions_found_try_a_customer_merchant.3e3710ba": "No Matching Transactions Found. Try A Customer, Merchant Or Reference.",
    "PlatformAdministrationView.change_transaction.4027ffa2": "Change Transaction",
    "PlatformAdministrationView.leave_empty_to_review_now.b1e7974f": "Leave Empty To Review Now.",
    "PlatformAdministrationView.search_for_a_transaction_to_begin.ef2fbcc2": "Search for a transaction to begin.",
    "PlatformAdministrationView.review_queue.83c3c922": "Review Queue",
    "PlatformAdministrationView.scheduled_requests_and_their_latest_eligibility_decisio.a06d335d": "Scheduled requests and their latest eligibility decisions.",
    "PlatformAdministrationView.review.aff0766a": "Review",
    "PlatformAdministrationView.review_time.e4270634": "Review Time",
    "PlatformAdministrationView.reason.f81ab834": "Reason",
    "PlatformAdministrationView.no_matching_reviews.2259a382": "No Matching Reviews",
    "PlatformAdministrationView.new_reviews_will_appear_here_with_their_latest_status.58787413": "New reviews will appear here with their latest status.",
    "PlatformAdministrationView.dispute_reconciliation.3e9a2ba2": "Dispute Reconciliation",
    "PlatformAdministrationView.unbalanced_journals.cf1df308": "Unbalanced Journals ·",
    "PlatformAdministrationView.balance_differences.f0ac062e": "Balance Differences",
    "PlatformAdministrationView.refresh_reports.210920a6": "Refresh Reports",
    "PlatformAdministrationView.accounts.8a7c8b67": "Accounts",
    "PlatformAdministrationView.payment_outcomes.3632653c": "Payment Outcomes",
    "PlatformAdministrationView.transactions.3e5136fd": "Transactions",
    "PlatformAdministrationView.no_matching_outcomes.d3a82497": "No matching outcomes.",
    "PlatformAdministrationView.country_policies.8d97b5cd": "Country Policies",
    "PlatformAdministrationView.country.701d021d": "Country",
    "PlatformAdministrationView.account_currency.ce36f1a3": "Account Currency",
    "PlatformAdministrationView.identity_requirements.b09085aa": "Identity Requirements",
    "PlatformAdministrationView.document_options_follow_the_selected_issuing_country_cu.d63f4225": "Document options follow the selected issuing country. Customers choose from these configured requirements.",
    "PlatformAdministrationView.advanced_validation.6ef5fe0f": "Advanced Validation",
    "PlatformAdministrationView.regions_and_address.bf14d83f": "Regions And Address",
    "PlatformAdministrationView.limits_and_fees.f76ad564": "Limits And Fees",
    "PlatformAdministrationView.regional_preferences.1a9509c0": "Regional Preferences",
    "PlatformAdministrationView.country_name_dialling_code_and_locale_are_filled_from_t.89f3c27e": "Country name, dialling code and locale are filled from the country catalog.",
    "PlatformAdministrationView.add_a_country.12776227": "Add A Country",
    "PlatformAdministrationView.select_a_country_to_fill_its_name_currency_dialling_cod.ed878799": "Select a country to fill its name, currency, dialling code, locale and time zone automatically.",
    "PlatformAdministrationView.new_country.83abee8c": "New Country",
    "PlatformAdministrationView.add_country.f4e335dd": "Add Country",
    "PlatformAdministrationView.new_countries_start_disabled_until_their_policies_are_r.9dd7ac06": "New countries start disabled until their policies are reviewed.",
    "PlatformAdministrationView.operating_country.2c09c386": "Operating Country",
    "PlatformAdministrationView.business_country.3559cd3c": "Business Country",
    "PlatformAdministrationView.refresh_history_and_keep_my_input.eb618b34": "Refresh History And Keep My Input",
    "PlatformAdministrationView.before_and_after.706bf7e0": "Before And After",
    "PlatformAdministrationView.version.dd167905": "Version",
    "PlatformAdministrationView._version.a73f1902": "→ Version",
    "PlatformAdministrationView._country.bd6c3500": ". Country:",
    "PlatformAdministrationView._maximum_accounts.1872ec9f": ". Maximum Accounts:",
    "PlatformAdministrationView._all_settings_are_published_as_one_complete_version.a9647593": ". All settings are published as one complete version.",
    "ProfileView.profile_security.f020efed": "Profile & Security",
    "ProfileView.profile_sections.35f2e218": "Profile Sections",
    "ProfileView.details.45989de4": "Details",
    "ProfileView.security.8f6fb4eb": "Security",
    "ProfileView.alerts.f62e9267": "Alerts",
    "ProfileView.personal_details.3a886778": "Personal Details",
    "ProfileView.email.969ccbd3": "Email",
    "ProfileView.mobile.cbaad3cf": "Mobile",
    "ProfileView.profile_registered.4ef00312": "Profile Registered",
    "ProfileView._optional.ab052003": "(Optional)",
    "ProfileView.residential_address.ab3b9e12": "Residential Address",
    "ProfileView.select.2a78025d": "Select",
    "ProfileView.country_of_residence.a5504bc6": "Country Of Residence",
    "ProfileView.save_personal_details.0999dd8f": "Save Personal Details",
    "ProfileView.identity_details.6a240947": "Identity Details",
    "ProfileView.date_of_birth.90952e89": "Date Of Birth",
    "ProfileView.issuing_country.57242e1a": "Issuing Country",
    "ProfileView.your_date_of_birth_is_validated_from_your_identity_docu.299a9362": "Your date of birth is validated from your identity document when saved.",
    "ProfileView.once_saved_your_document_details_are_locked_the_require.42703e44": "Once saved, your document details are locked. The requirements follow the selected issuing country.",
    "ProfileView.save_identity_details.2c462742": "Save Identity Details",
    "ProfileView.change_email_or_mobile.18f6c6dd": "Change Email Or Mobile",
    "ProfileView.contact_verification_will_be_available_soon.f66dea72": "Contact verification will be available soon.",
    "ProfileView.email_address.09bf25ef": "Email Address",
    "ProfileView.mobile_number.34975ecb": "Mobile Number",
    "ProfileView.country_calling_code.5446eecf": "Country Calling Code",
    "ProfileView.sms.40b601a1": "SMS",
    "ProfileView.whatsapp.6a40edf1": "WhatsApp",
    "ProfileView.code_requested_for.e2b3e8fe": "Code requested for",
    "ProfileView._valid_until.722cc750": ". Valid until",
    "ProfileView.request_a_new_code.cb330557": "Request A New Code",
    "RegistrationView.dispute_portal.0fed13ec": "Dispute Portal",
    "RegistrationView.secure_from_the_start.36ddd4a1": "SECURE FROM THE START",
    "RegistrationView.one_secure_place_for_your_accounts_payments_and_dispute.58043b5e": "One secure place for your accounts, payments and dispute updates.",
    "RegistrationView.back_to_sign_in.ef5889ce": "Back To Sign-In",
    "RegistrationView.create_your_account.aeae4098": "CREATE YOUR ACCOUNT",
    "RegistrationView.try_again.df0fe9e0": "Try Again",
    "RegistrationView.registration_progress.54cb614b": "Registration Progress",
    "RegistrationView.1_your_details.ca0151eb": "1 · Your details",
    "RegistrationView.2_secure_your_account.e1871a27": "2 · Secure your account",
    "RegistrationView.first_name.96ce6e61": "First Name",
    "RegistrationView.surname.f762da05": "Surname",
    "RegistrationView.country_of_residence.a5504bc6": "Country Of Residence",
    "RegistrationView.email_address.09bf25ef": "Email Address",
    "RegistrationView.mobile_country_code.90557b1d": "Mobile Country Code",
    "RegistrationView.mobile_number.34975ecb": "Mobile Number",
    "RegistrationView.your_mobile_country_code_can_differ_from_your_country_o.a65979be": "Your mobile country code can differ from your country of residence.",
    "RegistrationView.banking_country.16238e38": "Banking Country",
    "RegistrationView.account_currency.ce36f1a3": "Account Currency",
    "RegistrationView.identity_document_issued_by.7371eb73": "Identity Document Issued By",
    "RegistrationView.identity_document.f72831a4": "Identity Document",
    "RegistrationView.number.bd82cf16": "Number",
    "RegistrationView.send_verification_code_by.1f7d8111": "Send Verification Code By",
    "RegistrationView.verification_is_temporarily_unavailable_please_try_agai.37ad6540": "Verification is temporarily unavailable. Please try again shortly.",
    "RegistrationView.create_a_password.208deef7": "Create A Password",
    "RegistrationView.confirm_password.c292210c": "Confirm Password",
    "RegistrationView.at_least.3251982c": "At least",
    "RegistrationView.characters_with_upper_and_lower_case_letters_a_number_a.84214919": "characters, with upper and lower case letters, a number and a symbol.",
    "RegistrationView.back_to_your_details.179c4a71": "Back To Your Details",
    "RegistrationView.six_digit_verification_code.ae4a65c7": "Six-Digit Verification Code",
    "RegistrationView.your_code_expires_in_5_minutes.553acaf4": "Your code expires in 5 minutes.",
    "RegistrationView.start_again.0ec60501": "Start Again",
    "SignInView.dispute_portal.0fed13ec": "Dispute Portal",
    "SignInView.clear_answers_every_step.80c5e999": "CLEAR ANSWERS, EVERY STEP",
    "SignInView.review_your_transactions_raise_a_dispute_and_follow_the.224a279b": "Review your transactions, raise a dispute and follow the progress with confidence.",
    "SignInView.built_for_peace_of_mind.7e435959": "Built for peace of mind",
    "SignInView.customer_staff_portal.53ff07d1": "CUSTOMER & STAFF PORTAL",
    "SignInView.your_account_is_verified_sign_in_to_continue.315f22ac": "Your account is verified. Sign in to continue.",
    "SignInView.your_password_has_been_updated_all_devices_have_been_si.49e80d25": "Your password has been updated. All devices have been signed out. Sign in with your new password.",
    "SignInView.your_contact_details_are_verified_and_saved_sign_in_wit.747ae96a": "Your contact details are verified and saved. Sign in with your registered contact.",
    "SignInView.email_or_mobile_number.89c2cbc7": "Email Or Mobile Number",
    "SignInView.enter_your_email_or_mobile_number.bad0d696": "Enter Your Email Or Mobile Number",
    "SignInView.password.e7cf3ef4": "Password",
    "SignInView.enter_your_password.c06bf967": "Enter your password",
    "SignInView.1_details_verified.ed65f003": "1 · Details verified",
    "SignInView.2_security_check.362b62bc": "2 · Security check",
    "SignInView.verification_code.4c71415f": "Verification Code",
    "SignInView.your_code_expires_at.5864ce18": "Your code expires at",
    "SignInView._never_share_your_code.770ed3b7": ". Never share your code.",
    "SignInView.personal_devices_only.9c772830": "Personal Devices Only ·",
    "SignInView.days.e08c0aa8": "Days",
    "SignInView.didn_t_receive_the_otp.e6a12dab": "Didn’t receive the OTP?",
    "SignInView.back_to_sign_in.ef5889ce": "Back To Sign-In",
    "StaffTransactionDetailView.back_to_transactions.faef2562": "Back To Transactions",
    "StaffTransactionDetailView.customer_assistance.f774b9a3": "Customer Assistance",
    "StaffTransactionDetailView.loading_transaction.ad395b31": "Loading transaction…",
    "StaffTransactionDetailView.create_dispute_for_customer.85e9152e": "Create Dispute For Customer",
    "StaffTransactionDetailView.record_the_customer_s_concern_the_case_will_be_attribut.ec7a34c9": "Record the customer’s concern. The case will be attributed to you and routed to qualified staff.",
    "StaffTransactionDetailView.this_transaction_is_available_as_history_and_cannot_sta.13de8ab2": "This transaction is available as history and cannot start a new dispute.",
    "StaffTransactionsView.customer_transactions.4ffe9da0": "Customer Transactions",
    "StaffTransactionsView.search_customer_transactions.ffd90080": "Search Customer Transactions",
    "StaffTransactionsView.customer_contact_merchant_or_reference.2476dec5": "Customer, contact, merchant or reference",
    "StaffTransactionsView.search_transactions.32b5cb7a": "Search Transactions",
    "StaffTransactionsView.retry.942087cc": "Retry",
    "StaffTransactionsView.transactions.81dc075c": "transactions",
    "StaffTransactionsView.loading_transactions.d69971d3": "Loading transactions…",
    "StaffTransactionsView.case_linked.f87d2125": "Case Linked",
    "StaffTransactionsView.pending.331551b0": "Pending",
    "StaffTransactionsView.reversed.2154c60d": "Reversed",
    "StaffTransactionsView.no_transactions_match_your_filters.87ca4d47": "No transactions match your filters.",
    "StaffTransactionsView.previous.a57b08a4": "Previous",
    "StaffTransactionsView.page.0a30a815": "Page",
    "StaffTransactionsView.next.1ff57a29": "Next",
    "SupportView.message_queue_filters.a4be63a1": "Message Queue Filters",
    "SupportView.closed.c21ead06": "Closed",
    "SupportView.all_conversations.1b5fb8eb": "All Conversations",
    "SupportView.unread.1b9f384c": "Unread",
    "SupportView.case_conversations.458aa0d5": "Case Conversations",
    "SupportView.retry.942087cc": "Retry",
    "SupportView.search_conversations.4a322e26": "Search Conversations",
    "SupportView.clear_conversation_search.f622a8ef": "Clear Conversation Search",
    "SupportView.find_conversations.3469e79f": "Find Conversations",
    "SupportView.conversations.1d432f58": "Conversations",
    "SupportView.loading_messages.3a496d42": "Loading messages…",
    "SupportView.conversation_pages.65ab05d5": "Conversation Pages",
    "SupportView.page.0a30a815": "Page",
    "SupportView.of.28391d3b": "of",
    "SupportView.previous.a57b08a4": "Previous",
    "SupportView.next.1ff57a29": "Next",
    "SupportView.your_conversations_together.a0f121e1": "Your Conversations, Together",
    "SupportView.select_a_conversation_to_view_its_case_context_messages.d7e2bc40": "Select a conversation to view its case context, messages and latest updates.",
    "TransactionDetailView.back_to_transactions.faef2562": "Back To Transactions",
    "TransactionDetailView.transaction_unavailable.f8c5a93b": "Transaction Unavailable",
    "TransactionDetailView.transaction.eec26ddd": "Transaction",
    "TransactionDetailView.refund_reference.1bf879e3": "Refund reference",
    "TransactionDetailView.copy_refund_reference.87f3a23f": "Copy Refund Reference",
    "TransactionDetailView.type.baaddf70": "Type",
    "TransactionDetailView.disputes.110fa2bb": "Disputes",
    "TransactionsView.transactions.3e5136fd": "Transactions",
    "TransactionsView.activity.38da1505": "Activity",
    "TransactionsView.search_transactions.32b5cb7a": "Search Transactions",
    "TransactionsView.search_transactions.0fa80362": "Search transactions",
    "TransactionsView.clear_search.81c2c4f3": "Clear Search",
    "TransactionsView.filters.546ebb8e": "Filters",
    "TransactionsView.transaction_filters.ab78ccae": "Transaction Filters",
    "TransactionsView.filter_transactions.da354407": "Filter Transactions",
    "TransactionsView.close_filters.191dde41": "Close Filters",
    "TransactionsView.direction.9c8a9579": "Direction",
    "TransactionsView.all_activity.34160ed0": "All Activity",
    "TransactionsView.money_out.cae0dcd4": "Money Out",
    "TransactionsView.money_in.8b640973": "Money In",
    "TransactionsView.type.baaddf70": "Type",
    "TransactionsView.transaction_type.e9c992fe": "Transaction Type",
    "TransactionsView.all_types.bc013254": "All Types",
    "TransactionsView.transaction_date_range.aff4515c": "Transaction Date Range",
    "TransactionsView.start_date.4c52faad": "Start Date",
    "TransactionsView.from_date.61288e82": "From Date",
    "TransactionsView.end_date.e970951c": "End Date",
    "TransactionsView.to_date.8f44d1ea": "To Date",
    "TransactionsView.sort.bec69036": "Sort",
    "TransactionsView.sort_by.6d222755": "Sort By",
    "TransactionsView.newest_first.4ea18857": "Newest First",
    "TransactionsView.oldest_first.c4a20dba": "Oldest First",
    "TransactionsView.highest_amount.583472d7": "Highest Amount",
    "TransactionsView.lowest_amount.622efb54": "Lowest Amount",
    "TransactionsView.reset.daee7606": "Reset",
    "TransactionsView.show_transactions.254f102a": "Show Transactions",
    "TransactionsView.clear_filters.5776503c": "Clear Filters",
    "TransactionsView.amounts_in_account_currency.e6842a8c": "Amounts in account currency",
    "TransactionsView.page.0a30a815": "Page",
    "TransactionsView.of.28391d3b": "of",
    "TransactionsView.previous.a57b08a4": "Previous",
    "TransactionsView.next.1ff57a29": "Next",
    "UserAdministrationView.super_admin.c2d77466": "SUPER ADMIN",
    "UserAdministrationView.users_access.d119c9e7": "Users & Access",
    "UserAdministrationView.users.7dfb4cf6": "users",
    "UserAdministrationView.retry.942087cc": "Retry",
    "UserAdministrationView.user_directory.f32b19fc": "User Directory",
    "UserAdministrationView.search_name_or_email.04bf7b77": "Search name or email",
    "UserAdministrationView.search_users.be6182a4": "Search Users",
    "UserAdministrationView.filter_users_by_role.1741d008": "Filter Users By Role",
    "UserAdministrationView.all_roles.78f133af": "All roles",
    "UserAdministrationView.filter_users_by_status.219b910f": "Filter Users By Status",
    "UserAdministrationView.loading_users.a53ec7ac": "Loading users…",
    "UserAdministrationView.you.08b04193": "You",
    "UserAdministrationView.no_matching_users.4d2b8a84": "No matching users",
    "UserAdministrationView.clear_filters.5776503c": "Clear Filters",
    "UserAdministrationView.previous.a57b08a4": "Previous",
    "UserAdministrationView.next.1ff57a29": "Next",
    "UserAdministrationView.close_user_editor.6a49f2a2": "Close User Editor",
    "UserAdministrationView._optional.ab052003": "(Optional)",
    "UserAdministrationView.names_can_be_completed_separately_from_access_changes.5cfd36a4": "Names can be completed separately from access changes.",
    "UserAdministrationView.role.14736a2e": "Role",
    "UserAdministrationView.12_characters_with_uppercase_lowercase_a_number_and_a_s.e4fbb148": "12+ characters with uppercase, lowercase, a number and a symbol.",
    "UserAdministrationView.account_status.f4d5f36f": "Account status",
    "UserAdministrationView.account_status.dafb01ab": "Account Status",
    "UserAdministrationView.open_cases.bf4e95fd": "open cases ·",
    "UserAdministrationView.update_your_own_name_in_profile_another_super_admin_mus.41684073": "Update your own name in Profile. Another super admin must change your access.",
    "UserAdministrationView.reassign_this_user_s_open_cases_before_changing_their_r.a8b0ab0a": "Reassign this user’s open cases before changing their role or status.",
    "dialogs.transfer.amount": "Amount",
    "dialogs.transfer.fee": "Fee",
    "dialogs.transfer.totalDebit": "Total Debit",
    "dialogs.transfer.expires": "Quote Expires",
    "dialogs.transfer.target": "To Your Account",
    "dialogs.transfer.edit": "Edit Transfer",
    "dialogs.account.country": "Country",
    "dialogs.account.currency": "Account Currency",
    "dialogs.choices.noMatches": "No Matching Options",
    "dialogs.choices.empty": "No Options Available",
    "dialogs.closure.title": "Close Your Account",
    "dialogs.closure.request": "Request Account Closure",
    "dialogs.closure.description": "We’ll verify the request and review any linked cases before closing access. Financial and security records may need to be retained.",
    "dialogs.closure.confirm": "Submit Closure Request",
    "dialogs.closure.continue": "Continue Securely",
    "dialogs.closure.keep": "Keep My Account",
    "common.password": "Current Password",
    "common.optional": "Optional",
    "common.code": "Verification Code",
    "dialogs.filters.transactions": "Transaction Filters",
    "dialogs.filters.disputes": "Dispute Filters",
    "dialogs.filters.messages": "Message Filters",
    "dialogs.filters.title": "Filters",
    "dialogs.filters.status": "Status",
    "dialogs.filters.priority": "Priority",
    "dialogs.filters.account": "Account",
    "dialogs.filters.allStatuses": "All Statuses",
    "dialogs.filters.open": "Open",
    "dialogs.filters.closed": "Closed",
    "dialogs.filters.allPriorities": "All Priorities",
    "dialogs.filters.allAccounts": "All My Accounts",
    "dialogs.filters.allConversations": "All Conversations",
    "dialogs.filters.unread": "Unread Only",
    "dialogs.filters.from": "Start Date",
    "dialogs.filters.to": "End Date",
    "AccountSwitcher.view_details.bd5bbe97": "View Details →",
    "AppShell.profile_security.f020efed": "Profile & Security",
    "CaseFinancials.view_previous_dispute.63def428": "View Previous Dispute",
    "CaseFinancials.recall_refund.55a42d83": "Recall Refund",
    "CaseFinancials.view_reinstated_dispute.103799ca": "View Reinstated Dispute",
    "CaseFinancials.reinstate_dispute.835b99ad": "Reinstate Dispute",
    "CaseOperationsPanel.reason_for_taking_over.753ee89d": "Reason For Taking Over",
    "CaseOperationsPanel.assignment_reason.c5aa5432": "Assignment Reason",
    "CaseOperationsPanel.reason_for_priority_or_team_change.9786f799": "Reason For Priority Or Team Change",
    "CaseOperationsPanel.first_response_sast.45a845ed": "First Response · SAST",
    "CaseOperationsPanel.resolution_sast.69a8efdd": "Resolution · SAST",
    "CaseOperationsPanel.reason_for_target_change.1a3521b7": "Reason For Target Change",
    "CaseOperationsPanel.internal_escalation_reason.077ea688": "Internal Escalation Reason",
    "CaseStatusCatalogue.customer_label.d7879eab": "Customer Label",
    "CaseStatusCatalogue.display_order.fde84850": "Display Order",
    "CaseStatusCatalogue.reason_for_change.f8eca0eb": "Reason For Change",
    "DisputeSecurityCheck.current_password.8ac76099": "Current Password",
    "DisputeSecurityCheck.verification_code.4c71415f": "Verification Code",
    "DisputeSubmissionForm.reason.f81ab834": "Reason",
    "DisputeSubmissionForm.review.aff0766a": "Review",
    "DisputeSubmissionForm.review_your_dispute.e28b1833": "Review Your Dispute",
    "DisputeSubmissionForm.edit_details.8cfa48f3": "Edit Details",
    "DisputeTimeline.updates_replies.72592576": "Updates & Replies",
    "EvidencePanel.attach_files.bae534e8": "Attach Files",
    "EvidencePanel.take_photo.1ca36a90": "Take Photo",
    "EvidencePanel.access_required.d27df137": "Access required",
    "EvidencePanel.view.dcc839a4": "View",
    "EvidencePanel.access.ec5ba0ab": "Access",
    "EvidencePanel.remove_file.b4e51ea4": "Remove File",
    "GuidedEnquiry.change_concern.719a65d4": "Change Concern",
    "GuidedEnquiry.about_this_case.3911c9f4": "About This Case",
    "GuidedEnquiry.something_else.e8a57d98": "Something Else",
    "GuidedEnquiry.choose_a_transaction.5e2aa50b": "Choose A Transaction",
    "GuidedEnquiry.it_is_about_something_else.7ecdc57c": "It Is About Something Else",
    "NotificationPreferences.important_messages.cd5f3413": "Important Messages",
    "PolicyVersionAdministration._priority.ad55d8f2": "· Priority",
    "PolicyVersionAdministration.start_at.88e6b923": "Start At",
    "PolicyVersionAdministration.end_at.18366b50": "End At",
    "PolicyVersionAdministration.reason.f81ab834": "Reason",
    "PolicyVersionAdministration.search_version_history.b59eedea": "Search Version History",
    "PolicyVersionAdministration.removal_reason.e08acb92": "Removal Reason",
    "RememberedDevices.current_password.8ac76099": "Current Password",
    "SettingsVersionHistory.search_history.66d7e4a1": "Search History",
    "SettingsVersionHistory.reason.f81ab834": "Reason",
    "AccessRecoveryView.verification_is_temporarily_unavailable_please_try_agai.37ad6540": "Verification is temporarily unavailable. Please try again shortly.",
    "AccountRequestsView.review_note.6e8e6813": "Review Note",
    "AdminDisputeView.assign.8ece895c": "Assign",
    "AdminDisputeView.case_messages.d4bb7b32": "Case Messages",
    "AdminDisputeView.this_note_will_never_appear_in_the_customer_s_portal.0bd925ab": "This note will never appear in the customer''s portal.",
    "AdminDisputeView.save_note.425f50a0": "Save Note",
    "AdminQueueView.manage_case.a240b3f1": "Manage Case",
    "DashboardView.view_all.ff5573a6": "View All",
    "DashboardView.all_disputes.4e7ca763": "All Disputes",
    "DashboardView.review_a_transaction.83e0ae4f": "Review A Transaction",
    "DisputeDetailView.escalated_for_senior_review.3392d5b8": "Escalated for senior review",
    "DisputesView.new_dispute.3cd23cc2": "New Dispute",
    "HomeView.open_account.378549d3": "Open Account",
    "HomeView.account.7e1b0d56": "Account",
    "HomeView.view_account.593f37f6": "View Account",
    "HomeView.view_transactions.35b02795": "View Transactions",
    "HomeView.transfer.dde8bef7": "Transfer",
    "HomeView.your_disputes.a5d5233b": "Your Disputes",
    "HomeView.contact_support.f8d47b82": "Contact Support",
    "MonitoringView.period.6e795d4d": "Period",
    "MonitoringView.server_failures.a31ebaa9": "Server Failures",
    "MonitoringView.average_duration.01f3e9de": "Average Duration",
    "NewDisputeView.track_your_dispute.977dd2e8": "Track Your Dispute",
    "NewDisputeView.back_to_transactions.faef2562": "Back To Transactions",
    "NewDisputeView.view_existing_dispute.dc6249d4": "View Existing Dispute",
    "PlatformAdministrationView.scheduled_reviews.02793e9f": "Scheduled Reviews",
    "PlatformAdministrationView.posted_journals.85d044c1": "Posted Journals",
    "PlatformAdministrationView.reconciliation.c24d50c6": "Reconciliation",
    "PlatformAdministrationView.review_action.203542a7": "Review Action",
    "PlatformAdministrationView.open_transaction.1c19e2c9": "Open Transaction",
    "PlatformAdministrationView.available_to_customers.12e415a9": "Available To Customers",
    "PlatformAdministrationView.number_format.b953d780": "Number Format",
    "PlatformAdministrationView.region_label.2b7b41a3": "Region Label",
    "PlatformAdministrationView.postal_label.53f01e08": "Postal Label",
    "PlatformAdministrationView.postal_code_required.17c991b0": "Postal Code Required",
    "PlatformAdministrationView.minimum.735beee2": "Minimum",
    "PlatformAdministrationView.maximum.66c9ab17": "Maximum",
    "PlatformAdministrationView.daily_limit.5a2f8b3d": "Daily Limit",
    "PlatformAdministrationView.fixed_fee.6f88993d": "Fixed Fee",
    "PlatformAdministrationView.percentage_fee.73adf760": "Percentage Fee",
    "PlatformAdministrationView.time_zone.2545516e": "Time Zone",
    "PlatformAdministrationView.reason_for_change.f8eca0eb": "Reason For Change",
    "ProfileView.first_name.96ce6e61": "First Name",
    "ProfileView.surname.f762da05": "Surname",
    "ProfileView.middle_name.ba644ed1": "Middle Name",
    "ProfileView.street_address.a2e3f225": "Street Address",
    "ProfileView.apartment_or_unit.640ab095": "Apartment Or Unit",
    "ProfileView.suburb.9e843db5": "Suburb",
    "ProfileView.city_or_town.0d7f5c39": "City Or Town",
    "ProfileView.postal_code.8198b202": "Postal Code",
    "ProfileView.document_type.b0668a86": "Document Type",
    "ProfileView.current_password.8ac76099": "Current Password",
    "ProfileView.i_confirm_these_identity_details_are_correct.b4d4d9b3": "I Confirm These Identity Details Are Correct.",
    "ProfileView.contact_to_update.b93deacd": "Contact To Update",
    "ProfileView.new.18fdd549": "New",
    "ProfileView.send_verification_by.4220dbbb": "Send Verification By",
    "ProfileView.six_digit_verification_code.ae4a65c7": "Six-Digit Verification Code",
    "RegistrationView.your_money.8f3fe5d8": "Your Money.",
    "RegistrationView.your_peace_of_mind.0d025749": "Your Peace Of Mind.",
    "RegistrationView.your_identity_details_are_encrypted_we_never_send_passw.c0fcb18a": "Your identity details are encrypted. We never send passwords in messages.",
    "SignInView.when_something.d3dbef5a": "When Something",
    "SignInView.doesn_t_look_right.636bd016": "Doesn''t Look Right.",
    "SignInView.secure_access.d1bc288f": "Secure access",
    "SignInView.forgot_password.eeca4f80": "Forgot Password?",
    "SignInView.don_t_require_an_otp_when_using_this_device.d79e26a6": "Don’t Require An OTP When Using This Device",
    "SignInView.new_here.38c1c445": "New here?",
    "SignInView.create_account.0dffe234": "Create Account",
    "StaffTransactionDetailView.open_linked_case.ffe56f3e": "Open Linked Case",
    "SupportView.new_enquiry.463829ea": "New Enquiry",
    "SupportView.last_message.9b0cddd1": "Last Message ·",
    "TransactionDetailView.return_to_activity.5d43df9c": "Return To Activity",
    "TransactionDetailView.date_time.2459ea42": "Date & time",
    "TransactionDetailView.description.526e0087": "Description",
    "TransactionDetailView.view_existing_dispute.dc6249d4": "View Existing Dispute",
    "TransactionDetailView.start_a_dispute.e07f1cee": "Start A Dispute",
    "TransactionDetailView.view_dispute_history.23cb7286": "View Dispute History",
    "TransactionDetailView.contact_support.f8d47b82": "Contact Support",
    "TransactionsView.private_to_you.c2dbbd29": "Private to you",
    "UserAdministrationView.add_user.115a26b5": "Add User",
    "UserAdministrationView.access_changes_are_audited.af5036a5": "Access changes are audited.",
    "UserAdministrationView.email_address.09bf25ef": "Email Address",
    "UserAdministrationView.first_name.96ce6e61": "First Name",
    "UserAdministrationView.middle_name.ba644ed1": "Middle Name",
    "UserAdministrationView.surname.f762da05": "Surname",
    "UserAdministrationView.initial_password.16b2fb8a": "Initial Password",
    "UserAdministrationView.case_capacity.efde6f70": "Case Capacity",
    "common.submitting": "Submitting…",
    "common.hidePassword": "Hide password",
    "common.showPassword": "Show password",
    "enquiry.selectCase": "Select Related Case",
    "enquiry.selectTransaction": "Select Related Transaction",
    "enquiry.findCase": "Find A Case",
    "enquiry.findTransaction": "Find A Transaction",
    "enquiry.caseReference": "Case Reference",
    "enquiry.merchantDescription": "Merchant Or Description",
    "enquiry.retry": "Retry Submission",
    "enquiry.submit": "Submit Enquiry",
    "support.queue": "Message Queue",
    "support.messages": "Messages",
    "support.noMatches": "No Matching Conversations",
    "support.noMessages": "No Messages Yet",
    "support.openQueue": "Open Case Queue",
    "support.viewDisputes": "View Your Disputes",
    "login.verification": "Two-Step Verification",
    "login.welcome": "Welcome Back",
    "login.verifying": "Verifying Securely…",
    "login.verify": "Verify & Sign In",
    "login.signIn": "Sign In Securely",
    "closure.closeReview": "Close Review",
    "closure.review": "Review Request",
    "closure.startReview": "Start Review",
    "closure.complete": "Complete Closure",
    "closure.decline": "Decline Request",
    "closure.saveDecision": "Save Decision",
    "case.startingReview": "Starting Review…",
    "case.startReview": "Start Review",
    "case.internalNote": "INTERNAL NOTE",
    "case.customerUpdate": "CUSTOMER UPDATE",
    "case.moveForward": "MOVE CASE FORWARD",
    "case.reviewAgain": "REVIEW AGAIN",
    "case.updateStatus": "Update Status",
    "case.reopen": "Reopen Case",
    "case.updateOutcome": "Customer Update Or Outcome",
    "case.reopenReason": "Reason For Reopening",
    "case.saveStatus": "Save Status Update",
    "case.assigning": "Assigning…",
    "accounts.loading": "Loading Your Accounts",
    "accounts.empty": "No Accounts Yet",
    "notifications.markRead": "Mark As Read",
    "case.alreadyOpen": "Dispute Already Open",
    "case.unavailable": "Dispute Unavailable",
    "dialogs.filters.close": "Close Filters",
    "navigation.overview": "Overview",
    "navigation.transactions": "Transactions",
    "navigation.disputes": "Disputes",
    "navigation.support": "Support",
    "navigation.profile": "Profile",
    "navigation.cases": "Cases",
    "navigation.policy": "Policy",
    "navigation.operations": "Dispute Operations",
    "navigation.monitoring": "API Monitoring",
    "navigation.users": "User Administration",
    "navigation.closures": "Account Closure Requests",
    "cases.active": "Active Cases",
    "cases.completed": "Completed Cases",
    "cases.all": "All Cases",
    "filters.all": "All",
    "filters.dateRange": "Date range",
    "cases.noMatches": "No matching disputes",
    "cases.noReplies": "No replies needed",
    "cases.noClosed": "No closed cases",
    "cases.empty": "No disputes here",
    "activity.CaseTargetsChanged": "Target dates updated",
    "activity.CaseAssigned": "Specialist assigned",
    "activity.CaseTriaged": "Review Details Updated",
    "activity.CaseEscalated": "Senior review requested",
    "activity.CustomerUpdate": "Case update",
    "activity.Reopened": "Dispute reopened",
    "activity.EvidenceSubmitted": "Evidence submitted",
    "activity.EvidenceRemoved": "Evidence Removed",
    "activity.CustomerMessage": "Customer message",
    "activity.AgentReply": "Specialist reply",
    "activity.AutomaticDecision": "Automatic decision",
    "activity.DisputeRefundApproved": "Refund approved",
    "activity.DisputeRefundPosted": "Refund posted",
    "activity.DisputeRefundRecalled": "Refund recalled",
    "activity.DisputeReinstated": "Dispute reinstated",
    "activity.CaseUnassigned": "Case unassigned",
    "case.submit": "Submit Dispute",
    "common.offline": "Offline",
    "common.sending": "Sending…",
    "support.earlier": "Earlier Messages",
    "support.newAvailable": "New Messages Available · ",
    "support.you": "You",
    "support.staff": "Staff",
    "support.customer": "Customer",
    "support.send": "Send",
    "login.codeSent": "Enter the verification code sent to {destination}.",
    "login.intro": "Your secure dispute workspace.",
    "login.resendIn": "Resend In {time}",
    "login.resend": "Resend OTP",
    "contact.email": "Email Address",
    "contact.mobile": "Mobile Number",
    "contact.emailPlaceholder": "you@example.com",
    "contact.mobilePlaceholder": "Mobile number",
    "contact.verify": "Verify & Save Contact",
    "contact.sendCode": "Send Verification Code"
  },
  "ClientOptions": {
    "requestTimeoutMilliseconds": 15000,
    "transactionPageSize": 8,
    "disputePageSize": 12,
    "staffPageSize": 20,
    "notificationPageSize": 20,
    "reviewTransactionPageSize": 12
  }
}',
    N'Initial portal interface configuration.',
    N'2026-11-10T15:14:26.4586557Z'
);
INSERT INTO [configuration].[portalSettings] (
    [id],
    [createdById],
    [publicId],
    [code],
    [version],
    [body],
    [changeReason],
    [createdAt]
)
VALUES (
    N'2',
    NULL,
    N'CBA562C8-D315-41BC-B1AB-536311A252BB',
    N'Validation',
    N'1',
    N'{
  "maximumSettingsBytes": 1048576,
  "maximumLabelLength": 200,
  "maximumDescriptionLength": 2000,
  "maximumKeyLength": 64,
  "maximumPromptLength": 2000,
  "maximumPhaseLength": 100,
  "initialControlKind": "concern",
  "relatedCaseControlKind": "relation",
  "reviewControlKind": "review",
  "enquiryControlKinds": [
    "relation",
    "concern",
    "optional-transaction",
    "transaction",
    "case",
    "description",
    "date",
    "additional",
    "review"
  ],
  "completionFields": [
    "description",
    "dateAnswered",
    "additionalAnswered"
  ],
  "concernFlags": [
    "collectDate"
  ],
  "clientOptions": {
    "requestTimeoutMilliseconds": {
      "minimum": 100,
      "maximum": 120000
    },
    "transactionPageSize": {
      "minimum": 1,
      "maximum": 100
    },
    "disputePageSize": {
      "minimum": 1,
      "maximum": 100
    },
    "staffPageSize": {
      "minimum": 1,
      "maximum": 100
    },
    "notificationPageSize": {
      "minimum": 1,
      "maximum": 100
    },
    "reviewTransactionPageSize": {
      "minimum": 1,
      "maximum": 100
    }
  }
}',
    N'Initial configurable portal validation limits.',
    N'2026-11-10T15:14:30.7274824Z'
);
INSERT INTO [configuration].[portalSettings] (
    [id],
    [createdById],
    [publicId],
    [code],
    [version],
    [body],
    [changeReason],
    [createdAt]
)
VALUES (
    N'3',
    NULL,
    N'B064D1AD-6E82-4C8A-BFD6-9C6D0ED3BBF4',
    N'Interface',
    N'2',
    N'{
  "roles": {
    "service_clerk": {
      "label": "Service Clerk",
      "description": "View customer and transaction details and submit disputes on behalf of clients. No refund or specialist assignment rights.",
      "rank": 7
    },
    "customer": {
      "label": "Customer",
      "description": "Own accounts, transactions and disputes.",
      "rank": 9
    },
    "dispute_agent": {
      "label": "Dispute Agent",
      "description": "General dispute reviews and customer replies.",
      "rank": 6
    },
    "fraud_agent": {
      "label": "Fraud Specialist",
      "description": "Unauthorised activity and fraud investigations.",
      "rank": 2
    },
    "card_specialist": {
      "label": "Card Specialist",
      "description": "Card purchases, duplicates and merchant disputes.",
      "rank": 3
    },
    "debit_order_specialist": {
      "label": "Debit-Order Specialist",
      "description": "Mandates, collections and cancellation disputes.",
      "rank": 4
    },
    "payments_specialist": {
      "label": "Payments Investigator",
      "description": "Transfer and cash-withdrawal investigations.",
      "rank": 5
    },
    "operations_manager": {
      "label": "Operations Manager",
      "description": "Assign cases, manage priorities and supervise reviews.",
      "rank": 1
    },
    "super_admin": {
      "label": "Super Admin",
      "description": "Manage users, roles, dispute rules and case assignments.",
      "rank": 0
    },
    "audit_reader": {
      "label": "Audit Reader",
      "description": "Read cases and audit history without changing them.",
      "rank": 8
    }
  },
  "queues": {
    "General": {
      "label": "Dispute Support",
      "steps": [
        "Confirm the transaction and customer concern",
        "Request any missing evidence",
        "Record the decision and customer outcome"
      ]
    },
    "Fraud": {
      "label": "Fraud Investigation",
      "steps": [
        "Review unauthorised activity and account risk",
        "Coordinate account protection through approved bank channels",
        "Document investigation evidence and the outcome"
      ]
    },
    "Card": {
      "label": "Card Disputes",
      "steps": [
        "Check the card transaction and merchant evidence",
        "Assess duplicate, amount or goods-and-services claims",
        "Record the scheme review outcome without promising a refund"
      ]
    },
    "DebitOrder": {
      "label": "Debit-Order Disputes",
      "steps": [
        "Review the mandate and cancellation evidence",
        "Check collection date and applicable dispute policy",
        "Record the specialist decision and explain the outcome"
      ]
    },
    "Payments": {
      "label": "Payments Investigations",
      "steps": [
        "Trace the transfer or cash withdrawal reference",
        "Reconcile settlement or cash-dispensing evidence",
        "Record the investigation result and customer outcome"
      ]
    }
  },
  "priorities": {
    "1": "Low",
    "2": "Normal",
    "3": "Medium",
    "4": "High",
    "5": "Critical"
  },
  "severities": [
    "Low",
    "Medium",
    "High",
    "Critical"
  ],
  "transactionStatuses": {
    "Posted": "Completed",
    "Pending": "Pending Payment",
    "Reversed": "Reversed"
  },
  "availability": [
    "Available",
    "Busy",
    "Offline"
  ],
  "staffStatuses": [
    "Active",
    "Locked",
    "Disabled"
  ],
  "paymentStatuses": {
    "AwaitingApproval": "Awaiting External Confirmation",
    "Preparing": "Preparing",
    "Processing": "Processing",
    "Completed": "Completed",
    "Failed": "Failed",
    "Cancelled": "Cancelled"
  },
  "EnquiryFlow": {
    "initialStep": "concern",
    "relatedCaseInitialStep": "relation",
    "reviewStep": "review",
    "steps": [
      {
        "code": "relation",
        "kind": "relation",
        "phase": "Concern",
        "prompt": "Would you like to ask about this case or discuss something different?",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "concern",
        "kind": "concern",
        "phase": "Concern",
        "prompt": "Choose what you need help with.",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "optional-transaction",
        "kind": "optional-transaction",
        "phase": "Details",
        "prompt": "Is this about a transaction on your account?",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "transaction",
        "kind": "transaction",
        "phase": "Details",
        "prompt": "Choose the transaction you need help with.",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "case",
        "kind": "case",
        "phase": "Details",
        "prompt": "Choose the dispute you want to discuss.",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "description",
        "kind": "description",
        "phase": "Details",
        "prompt": "Tell us what happened and how we can help.",
        "completionField": "description",
        "concernFlag": null
      },
      {
        "code": "date",
        "kind": "date",
        "phase": "Details",
        "prompt": "When did this issue happen? You can skip this if you are unsure.",
        "completionField": "dateAnswered",
        "concernFlag": "collectDate"
      },
      {
        "code": "additional",
        "kind": "additional",
        "phase": "Details",
        "prompt": "Is there anything else you would like our team to know?",
        "completionField": "additionalAnswered",
        "concernFlag": null
      },
      {
        "code": "review",
        "kind": "review",
        "phase": "Review",
        "prompt": "Please check your enquiry before sending it.",
        "completionField": null,
        "concernFlag": null
      }
    ]
  },
  "ClientOptions": {
    "requestTimeoutMilliseconds": 15000,
    "transactionPageSize": 8,
    "disputePageSize": 12,
    "staffPageSize": 20,
    "notificationPageSize": 20,
    "reviewTransactionPageSize": 12
  }
}',
    N'Restore fixed interface copy to Vue and move evidence rules to their database tables.',
    N'2026-11-10T15:14:30.7434758Z'
);
INSERT INTO [configuration].[portalSettings] (
    [id],
    [createdById],
    [publicId],
    [code],
    [version],
    [body],
    [changeReason],
    [createdAt]
)
VALUES (
    N'4',
    NULL,
    N'2177CE27-BC24-4EF7-8F01-269CA59916F7',
    N'Interface',
    N'3',
    N'{
  "roles": {
    "service_clerk": {
      "label": "Service Clerk",
      "description": "View customer and transaction details and submit disputes on behalf of clients. No refund or specialist assignment rights.",
      "rank": 7
    },
    "customer": {
      "label": "Customer",
      "description": "Own accounts, transactions and disputes.",
      "rank": 9
    },
    "dispute_agent": {
      "label": "Dispute Agent",
      "description": "General dispute reviews and customer replies.",
      "rank": 6
    },
    "fraud_agent": {
      "label": "Fraud Specialist",
      "description": "Unauthorised activity and fraud investigations.",
      "rank": 2
    },
    "card_specialist": {
      "label": "Card Specialist",
      "description": "Card purchases, duplicates and merchant disputes.",
      "rank": 3
    },
    "debit_order_specialist": {
      "label": "Debit-Order Specialist",
      "description": "Mandates, collections and cancellation disputes.",
      "rank": 4
    },
    "payments_specialist": {
      "label": "Payments Investigator",
      "description": "Transfer and cash-withdrawal investigations.",
      "rank": 5
    },
    "operations_manager": {
      "label": "Operations Manager",
      "description": "Assign cases, manage priorities and supervise reviews.",
      "rank": 1
    },
    "super_admin": {
      "label": "Super Admin",
      "description": "Manage users, roles, dispute rules and case assignments.",
      "rank": 0
    },
    "audit_reader": {
      "label": "Audit Reader",
      "description": "Read cases and audit history without changing them.",
      "rank": 8
    }
  },
  "queues": {
    "General": {
      "label": "Dispute Support",
      "steps": [
        "Confirm the transaction and customer concern",
        "Request any missing evidence",
        "Record the decision and customer outcome"
      ]
    },
    "Fraud": {
      "label": "Fraud Investigation",
      "steps": [
        "Review unauthorised activity and account risk",
        "Coordinate account protection through approved bank channels",
        "Document investigation evidence and the outcome"
      ]
    },
    "Card": {
      "label": "Card Disputes",
      "steps": [
        "Check the card transaction and merchant evidence",
        "Assess duplicate, amount or goods-and-services claims",
        "Record the scheme review outcome without promising a refund"
      ]
    },
    "DebitOrder": {
      "label": "Debit-Order Disputes",
      "steps": [
        "Review the mandate and cancellation evidence",
        "Check collection date and applicable dispute policy",
        "Record the specialist decision and explain the outcome"
      ]
    },
    "Payments": {
      "label": "Payments Investigations",
      "steps": [
        "Trace the transfer or cash withdrawal reference",
        "Reconcile settlement or cash-dispensing evidence",
        "Record the investigation result and customer outcome"
      ]
    }
  },
  "priorities": {
    "1": "Low",
    "2": "Normal",
    "3": "Medium",
    "4": "High",
    "5": "Critical"
  },
  "severities": [
    "Low",
    "Medium",
    "High",
    "Critical"
  ],
  "transactionStatuses": {
    "Posted": "Completed",
    "Pending": "Pending Payment",
    "Reversed": "Reversed"
  },
  "availability": [
    "Available",
    "Busy",
    "Offline"
  ],
  "staffStatuses": [
    "Active",
    "Locked",
    "Disabled"
  ],
  "paymentStatuses": {
    "AwaitingApproval": "Awaiting External Confirmation",
    "Preparing": "Preparing",
    "Processing": "Processing",
    "Completed": "Completed",
    "Failed": "Failed",
    "Cancelled": "Cancelled"
  },
  "EnquiryFlow": {
    "initialStep": "concern",
    "relatedCaseInitialStep": "relation",
    "reviewStep": "review",
    "steps": [
      {
        "code": "relation",
        "kind": "relation",
        "phase": "Concern",
        "prompt": "Would you like to ask about this case or discuss something different?",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "concern",
        "kind": "concern",
        "phase": "Concern",
        "prompt": "Choose what you need help with.",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "optional-transaction",
        "kind": "optional-transaction",
        "phase": "Details",
        "prompt": "Is this about a transaction on your account?",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "transaction",
        "kind": "transaction",
        "phase": "Details",
        "prompt": "Choose the transaction you need help with.",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "case",
        "kind": "case",
        "phase": "Details",
        "prompt": "Choose the dispute you want to discuss.",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "description",
        "kind": "description",
        "phase": "Details",
        "prompt": "Tell us what happened and how we can help.",
        "completionField": "description",
        "concernFlag": null
      },
      {
        "code": "date",
        "kind": "date",
        "phase": "Details",
        "prompt": "When did this issue happen? You can skip this if you are unsure.",
        "completionField": "dateAnswered",
        "concernFlag": "collectDate"
      },
      {
        "code": "additional",
        "kind": "additional",
        "phase": "Details",
        "prompt": "Is there anything else you would like our team to know?",
        "completionField": "additionalAnswered",
        "concernFlag": null
      },
      {
        "code": "review",
        "kind": "review",
        "phase": "Review",
        "prompt": "Please check your enquiry before sending it.",
        "completionField": null,
        "concernFlag": null
      }
    ]
  },
  "ClientOptions": {
    "requestTimeoutMilliseconds": 15000,
    "transactionPageSize": 8,
    "disputePageSize": 12,
    "staffPageSize": 20,
    "notificationPageSize": 20,
    "reviewTransactionPageSize": 12
  }
,"customerSearchEnabled":true}',
    N'Enable database-managed customer search controls.',
    N'2026-11-10T15:14:31.1241086Z'
);
INSERT INTO [configuration].[portalSettings] (
    [id],
    [createdById],
    [publicId],
    [code],
    [version],
    [body],
    [changeReason],
    [createdAt]
)
VALUES (
    N'5',
    NULL,
    N'5812310C-8E8D-488D-ABA2-54944DF8C7D0',
    N'Interface',
    N'4',
    N'{
  "roles": {
    "service_clerk": {
      "label": "Service Clerk",
      "description": "View customer and transaction details and submit disputes on behalf of clients. No refund or specialist assignment rights.",
      "rank": 7
    },
    "customer": {
      "label": "Customer",
      "description": "Own accounts, transactions and disputes.",
      "rank": 9
    },
    "dispute_agent": {
      "label": "Dispute Agent",
      "description": "General dispute reviews and customer replies.",
      "rank": 6
    },
    "fraud_agent": {
      "label": "Fraud Specialist",
      "description": "Unauthorised activity and fraud investigations.",
      "rank": 2
    },
    "card_specialist": {
      "label": "Card Specialist",
      "description": "Card purchases, duplicates and merchant disputes.",
      "rank": 3
    },
    "debit_order_specialist": {
      "label": "Debit-Order Specialist",
      "description": "Mandates, collections and cancellation disputes.",
      "rank": 4
    },
    "payments_specialist": {
      "label": "Payments Investigator",
      "description": "Transfer and cash-withdrawal investigations.",
      "rank": 5
    },
    "operations_manager": {
      "label": "Operations Manager",
      "description": "Assign cases, manage priorities and supervise reviews.",
      "rank": 1
    },
    "super_admin": {
      "label": "Super Admin",
      "description": "Manage users, roles, dispute rules and case assignments.",
      "rank": 0
    },
    "audit_reader": {
      "label": "Audit Reader",
      "description": "Read cases and audit history without changing them.",
      "rank": 8
    }
  },
  "queues": {
    "General": {
      "label": "Dispute Support",
      "steps": [
        "Confirm the transaction and customer concern",
        "Request any missing evidence",
        "Record the decision and customer outcome"
      ]
    },
    "Fraud": {
      "label": "Fraud Investigation",
      "steps": [
        "Review unauthorised activity and account risk",
        "Coordinate account protection through approved bank channels",
        "Document investigation evidence and the outcome"
      ]
    },
    "Card": {
      "label": "Card Disputes",
      "steps": [
        "Check the card transaction and merchant evidence",
        "Assess duplicate, amount or goods-and-services claims",
        "Record the scheme review outcome without promising a refund"
      ]
    },
    "DebitOrder": {
      "label": "Debit-Order Disputes",
      "steps": [
        "Review the mandate and cancellation evidence",
        "Check collection date and applicable dispute policy",
        "Record the specialist decision and explain the outcome"
      ]
    },
    "Payments": {
      "label": "Payments Investigations",
      "steps": [
        "Trace the transfer or cash withdrawal reference",
        "Reconcile settlement or cash-dispensing evidence",
        "Record the investigation result and customer outcome"
      ]
    }
  },
  "priorities": {
    "1": "Low",
    "2": "Normal",
    "3": "Medium",
    "4": "High",
    "5": "Critical"
  },
  "severities": [
    "Low",
    "Medium",
    "High",
    "Critical"
  ],
  "transactionStatuses": {
    "Posted": "Completed",
    "Pending": "Pending Payment",
    "Reversed": "Reversed"
  },
  "availability": [
    "Available",
    "Busy",
    "Offline"
  ],
  "staffStatuses": [
    "Active",
    "Locked",
    "Disabled"
  ],
  "paymentStatuses": {
    "AwaitingApproval": "Awaiting External Confirmation",
    "Preparing": "Preparing",
    "Processing": "Processing",
    "Completed": "Completed",
    "Failed": "Failed",
    "Cancelled": "Cancelled"
  },
  "EnquiryFlow": {
    "initialStep": "concern",
    "relatedCaseInitialStep": "relation",
    "reviewStep": "review",
    "steps": [
      {
        "code": "relation",
        "kind": "relation",
        "phase": "Concern",
        "prompt": "Would you like to ask about this case or discuss something different?",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "concern",
        "kind": "concern",
        "phase": "Concern",
        "prompt": "Choose what you need help with.",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "optional-transaction",
        "kind": "optional-transaction",
        "phase": "Details",
        "prompt": "Is this about a transaction on your account?",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "transaction",
        "kind": "transaction",
        "phase": "Details",
        "prompt": "Choose the transaction you need help with.",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "case",
        "kind": "case",
        "phase": "Details",
        "prompt": "Choose the dispute you want to discuss.",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "description",
        "kind": "description",
        "phase": "Details",
        "prompt": "Tell us what happened and how we can help.",
        "completionField": "description",
        "concernFlag": null
      },
      {
        "code": "date",
        "kind": "date",
        "phase": "Details",
        "prompt": "When did this issue happen? You can skip this if you are unsure.",
        "completionField": "dateAnswered",
        "concernFlag": "collectDate"
      },
      {
        "code": "additional",
        "kind": "additional",
        "phase": "Details",
        "prompt": "Is there anything else you would like our team to know?",
        "completionField": "additionalAnswered",
        "concernFlag": null
      },
      {
        "code": "review",
        "kind": "review",
        "phase": "Review",
        "prompt": "Please check your enquiry before sending it.",
        "completionField": null,
        "concernFlag": null
      }
    ]
  },
  "ClientOptions": {
    "requestTimeoutMilliseconds": 15000,
    "transactionPageSize": 8,
    "disputePageSize": 12,
    "staffPageSize": 20,
    "notificationPageSize": 20,
    "reviewTransactionPageSize": 12
  }
,"customerSearchEnabled":false,"customerAccountOpeningEnabled":false}',
    N'Disable customer account opening and hide customer search and filters.',
    N'2026-11-10T15:14:31.1421072Z'
);
INSERT INTO [configuration].[portalSettings] (
    [id],
    [createdById],
    [publicId],
    [code],
    [version],
    [body],
    [changeReason],
    [createdAt]
)
VALUES (
    N'6',
    NULL,
    N'F2D6B21A-A671-4B62-86CA-1BD4A43F6353',
    N'Interface',
    N'5',
    N'{
  "roles": {
    "service_clerk": {
      "label": "Service Clerk",
      "description": "View customer and transaction details and submit disputes on behalf of clients. No refund or specialist assignment rights.",
      "rank": 7
    },
    "customer": {
      "label": "Customer",
      "description": "Own accounts, transactions and disputes.",
      "rank": 9
    },
    "dispute_agent": {
      "label": "Dispute Agent",
      "description": "General dispute reviews and customer replies.",
      "rank": 6
    },
    "fraud_agent": {
      "label": "Fraud Specialist",
      "description": "Unauthorised activity and fraud investigations.",
      "rank": 2
    },
    "card_specialist": {
      "label": "Card Specialist",
      "description": "Card purchases, duplicates and merchant disputes.",
      "rank": 3
    },
    "debit_order_specialist": {
      "label": "Debit-Order Specialist",
      "description": "Mandates, collections and cancellation disputes.",
      "rank": 4
    },
    "payments_specialist": {
      "label": "Payments Investigator",
      "description": "Transfer and cash-withdrawal investigations.",
      "rank": 5
    },
    "operations_manager": {
      "label": "Operations Manager",
      "description": "Assign cases, manage priorities and supervise reviews.",
      "rank": 1
    },
    "super_admin": {
      "label": "Super Admin",
      "description": "Manage users, roles, dispute rules and case assignments.",
      "rank": 0
    },
    "audit_reader": {
      "label": "Audit Reader",
      "description": "Read cases and audit history without changing them.",
      "rank": 8
    }
  },
  "queues": {
    "General": {
      "label": "Dispute Support",
      "steps": [
        "Confirm the transaction and customer concern",
        "Request any missing evidence",
        "Record the decision and customer outcome"
      ]
    },
    "Fraud": {
      "label": "Fraud Investigation",
      "steps": [
        "Review unauthorised activity and account risk",
        "Coordinate account protection through approved bank channels",
        "Document investigation evidence and the outcome"
      ]
    },
    "Card": {
      "label": "Card Disputes",
      "steps": [
        "Check the card transaction and merchant evidence",
        "Assess duplicate, amount or goods-and-services claims",
        "Record the scheme review outcome without promising a refund"
      ]
    },
    "DebitOrder": {
      "label": "Debit-Order Disputes",
      "steps": [
        "Review the mandate and cancellation evidence",
        "Check collection date and applicable dispute policy",
        "Record the specialist decision and explain the outcome"
      ]
    },
    "Payments": {
      "label": "Payments Investigations",
      "steps": [
        "Trace the transfer or cash withdrawal reference",
        "Reconcile settlement or cash-dispensing evidence",
        "Record the investigation result and customer outcome"
      ]
    }
  },
  "priorities": {
    "1": "Low",
    "2": "Normal",
    "3": "Medium",
    "4": "High",
    "5": "Critical"
  },
  "severities": [
    "Low",
    "Medium",
    "High",
    "Critical"
  ],
  "transactionStatuses": {
    "Posted": "Completed",
    "Pending": "Pending Payment",
    "Reversed": "Reversed"
  },
  "availability": [
    "Available",
    "Busy",
    "Offline"
  ],
  "staffStatuses": [
    "Active",
    "Locked",
    "Disabled"
  ],
  "paymentStatuses": {
    "AwaitingApproval": "Awaiting External Confirmation",
    "Preparing": "Preparing",
    "Processing": "Processing",
    "Completed": "Completed",
    "Failed": "Failed",
    "Cancelled": "Cancelled"
  },
  "EnquiryFlow": {
    "initialStep": "concern",
    "relatedCaseInitialStep": "relation",
    "reviewStep": "review",
    "steps": [
      {
        "code": "relation",
        "kind": "relation",
        "phase": "Concern",
        "prompt": "Would you like to ask about this case or discuss something different?",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "concern",
        "kind": "concern",
        "phase": "Concern",
        "prompt": "Choose what you need help with.",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "optional-transaction",
        "kind": "optional-transaction",
        "phase": "Details",
        "prompt": "Is this about a transaction on your account?",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "transaction",
        "kind": "transaction",
        "phase": "Details",
        "prompt": "Choose the transaction you need help with.",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "case",
        "kind": "case",
        "phase": "Details",
        "prompt": "Choose the dispute you want to discuss.",
        "completionField": null,
        "concernFlag": null
      },
      {
        "code": "description",
        "kind": "description",
        "phase": "Details",
        "prompt": "Tell us what happened and how we can help.",
        "completionField": "description",
        "concernFlag": null
      },
      {
        "code": "date",
        "kind": "date",
        "phase": "Details",
        "prompt": "When did this issue happen? You can skip this if you are unsure.",
        "completionField": "dateAnswered",
        "concernFlag": "collectDate"
      },
      {
        "code": "additional",
        "kind": "additional",
        "phase": "Details",
        "prompt": "Is there anything else you would like our team to know?",
        "completionField": "additionalAnswered",
        "concernFlag": null
      },
      {
        "code": "review",
        "kind": "review",
        "phase": "Review",
        "prompt": "Please check your enquiry before sending it.",
        "completionField": null,
        "concernFlag": null
      }
    ]
  }
,"customerSearchEnabled":false,"customerAccountOpeningEnabled":false,"clientOptions":{
    "requestTimeoutMilliseconds": 15000,
    "transactionPageSize": 8,
    "disputePageSize": 12,
    "staffPageSize": 20,
    "notificationPageSize": 20,
    "reviewTransactionPageSize": 12
  ,"transactionHistoryDays":30,"closedCaseHistoryDays":30}}',
    N'Configure the account-opening transaction window and recent closed-case visibility.',
    N'2026-11-10T15:14:31.1883482Z'
);
INSERT INTO [configuration].[portalSettings] (
    [id],
    [createdById],
    [publicId],
    [code],
    [version],
    [body],
    [changeReason],
    [createdAt]
)
VALUES (
    N'7',
    NULL,
    N'183C4D37-1AA9-474E-9BA8-CA166DD7FF91',
    N'Validation',
    N'2',
    N'{
  "maximumSettingsBytes": 1048576,
  "maximumLabelLength": 200,
  "maximumDescriptionLength": 2000,
  "maximumKeyLength": 64,
  "maximumPromptLength": 2000,
  "maximumPhaseLength": 100,
  "initialControlKind": "concern",
  "relatedCaseControlKind": "relation",
  "reviewControlKind": "review",
  "enquiryControlKinds": [
    "relation",
    "concern",
    "optional-transaction",
    "transaction",
    "case",
    "description",
    "date",
    "additional",
    "review"
  ],
  "completionFields": [
    "description",
    "dateAnswered",
    "additionalAnswered"
  ],
  "concernFlags": [
    "collectDate"
  ],
  "clientOptions": {
    "requestTimeoutMilliseconds": {
      "minimum": 100,
      "maximum": 120000
    },
    "transactionPageSize": {
      "minimum": 1,
      "maximum": 100
    },
    "disputePageSize": {
      "minimum": 1,
      "maximum": 100
    },
    "staffPageSize": {
      "minimum": 1,
      "maximum": 100
    },
    "notificationPageSize": {
      "minimum": 1,
      "maximum": 100
    },
    "reviewTransactionPageSize": {
      "minimum": 1,
      "maximum": 100
    }
  ,"transactionHistoryDays":{"minimum":1,"maximum":3650},"closedCaseHistoryDays":{"minimum":1,"maximum":3650}}
}',
    N'Configure the account-opening transaction window and recent closed-case visibility.',
    N'2026-11-10T15:14:31.1883482Z'
);
SET IDENTITY_INSERT [configuration].[portalSettings] OFF;
GO
SET IDENTITY_INSERT [configuration].[refundPolicies] ON;
GO
INSERT INTO [configuration].[refundPolicies] (
    [id],
    [policyId],
    [supersedesVersionId],
    [createdById],
    [deletedById],
    [publicId],
    [version],
    [priority],
    [effectiveFromUtc],
    [effectiveToUtc],
    [publishedAtUtc],
    [createdAtUtc],
    [recordedAtUtc],
    [changeReason],
    [isDeleted],
    [deletedAtUtc],
    [deletionReason],
    [requestId],
    [requestFingerprint],
    [sourceVersionPublicId],
    [provenance],
    [enabled],
    [maximumAmount],
    [maximumAgeHours],
    [dailyLimit],
    [duplicateWindowMinutes],
    [configurationKey],
    [initialVersion],
    [code],
    [scope],
    [lastVersion],
    [concurrencyToken]
)
VALUES (
    N'1',
    N'2',
    NULL,
    NULL,
    NULL,
    N'D04857CA-484A-492C-9EE7-AF72BA88BA92',
    N'1',
    N'0',
    N'2026-11-10T15:14:25.3885322Z',
    NULL,
    N'2026-11-10T15:14:25.3885322Z',
    NULL,
    N'2026-11-10T15:14:25.3885322Z',
    N'Current settings observed during migration; original publication attribution unknown.',
    N'0',
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    N'MigrationObservation',
    N'1',
    N'250.00',
    N'48',
    N'500.00',
    N'10',
    NULL,
    NULL,
    NULL,
    N'Global',
    NULL,
    NULL
);
INSERT INTO [configuration].[refundPolicies] (
    [id],
    [policyId],
    [supersedesVersionId],
    [createdById],
    [deletedById],
    [publicId],
    [version],
    [priority],
    [effectiveFromUtc],
    [effectiveToUtc],
    [publishedAtUtc],
    [createdAtUtc],
    [recordedAtUtc],
    [changeReason],
    [isDeleted],
    [deletedAtUtc],
    [deletionReason],
    [requestId],
    [requestFingerprint],
    [sourceVersionPublicId],
    [provenance],
    [enabled],
    [maximumAmount],
    [maximumAgeHours],
    [dailyLimit],
    [duplicateWindowMinutes],
    [configurationKey],
    [initialVersion],
    [code],
    [scope],
    [lastVersion],
    [concurrencyToken]
)
VALUES (
    N'2',
    N'2',
    NULL,
    NULL,
    NULL,
    N'145E86F5-6824-4601-8245-25ED168AEBC8',
    N'0',
    N'0',
    N'2026-11-10T15:14:29.4879800Z',
    NULL,
    NULL,
    NULL,
    N'2026-11-10T15:14:29.4879800Z',
    N'Initial policy definition retained during consolidation.',
    N'0',
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    N'InitialDefaults',
    N'1',
    N'250.00',
    N'48',
    N'500.00',
    N'10',
    N'1',
    N'1',
    N'Default',
    N'Global',
    N'1',
    N'1'
);
SET IDENTITY_INSERT [configuration].[refundPolicies] OFF;
GO
SET IDENTITY_INSERT [configuration].[riskPolicies] ON;
GO
INSERT INTO [configuration].[riskPolicies] (
    [id],
    [policyId],
    [supersedesVersionId],
    [createdById],
    [deletedById],
    [publicId],
    [version],
    [priority],
    [effectiveFromUtc],
    [effectiveToUtc],
    [publishedAtUtc],
    [createdAtUtc],
    [recordedAtUtc],
    [changeReason],
    [isDeleted],
    [deletedAtUtc],
    [deletionReason],
    [requestId],
    [requestFingerprint],
    [sourceVersionPublicId],
    [provenance],
    [enabled],
    [highValueThreshold],
    [extraCheckAfterDays],
    [debitOrderWindowDays],
    [debitOrderAppLimit],
    [configurationKey],
    [initialVersion],
    [code],
    [scope],
    [lastVersion],
    [concurrencyToken]
)
VALUES (
    N'1',
    N'2',
    NULL,
    NULL,
    NULL,
    N'67E92A32-6365-41FC-93CC-52FF102F02BB',
    N'1',
    N'0',
    N'2026-11-10T15:14:25.8976327Z',
    NULL,
    N'2026-11-10T15:14:25.8976327Z',
    NULL,
    N'2026-11-10T15:14:25.8976327Z',
    N'Current settings observed during migration; original publication attribution unknown.',
    N'0',
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    N'MigrationObservation',
    N'1',
    N'5000.00',
    N'30',
    N'60',
    N'800.00',
    NULL,
    NULL,
    NULL,
    N'Global',
    NULL,
    NULL
);
INSERT INTO [configuration].[riskPolicies] (
    [id],
    [policyId],
    [supersedesVersionId],
    [createdById],
    [deletedById],
    [publicId],
    [version],
    [priority],
    [effectiveFromUtc],
    [effectiveToUtc],
    [publishedAtUtc],
    [createdAtUtc],
    [recordedAtUtc],
    [changeReason],
    [isDeleted],
    [deletedAtUtc],
    [deletionReason],
    [requestId],
    [requestFingerprint],
    [sourceVersionPublicId],
    [provenance],
    [enabled],
    [highValueThreshold],
    [extraCheckAfterDays],
    [debitOrderWindowDays],
    [debitOrderAppLimit],
    [configurationKey],
    [initialVersion],
    [code],
    [scope],
    [lastVersion],
    [concurrencyToken]
)
VALUES (
    N'2',
    N'2',
    NULL,
    NULL,
    NULL,
    N'E6A6A06A-9E4E-410F-A330-534003A1C73A',
    N'0',
    N'0',
    N'2026-11-10T15:14:26.6975714Z',
    NULL,
    NULL,
    NULL,
    N'2026-11-10T15:14:26.6975714Z',
    N'Initial policy definition retained during consolidation.',
    N'0',
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    N'InitialDefaults',
    N'1',
    N'5000.00',
    N'30',
    N'60',
    N'800.00',
    N'2',
    N'1',
    N'Default',
    N'Global',
    N'1',
    N'1'
);
SET IDENTITY_INSERT [configuration].[riskPolicies] OFF;
GO
SET IDENTITY_INSERT [configuration].[transactionTypes] ON;
GO
INSERT INTO [configuration].[transactionTypes] (
    [code],
    [displayName],
    [sortOrder],
    [id]
)
VALUES (
    N'CardPurchase',
    N'Card Purchase',
    N'1',
    N'45'
);
INSERT INTO [configuration].[transactionTypes] (
    [code],
    [displayName],
    [sortOrder],
    [id]
)
VALUES (
    N'CashWithdrawal',
    N'Cash Withdrawal',
    N'4',
    N'46'
);
INSERT INTO [configuration].[transactionTypes] (
    [code],
    [displayName],
    [sortOrder],
    [id]
)
VALUES (
    N'DebitOrder',
    N'Debit Order',
    N'2',
    N'47'
);
INSERT INTO [configuration].[transactionTypes] (
    [code],
    [displayName],
    [sortOrder],
    [id]
)
VALUES (
    N'Deposit',
    N'Deposit',
    N'5',
    N'48'
);
INSERT INTO [configuration].[transactionTypes] (
    [code],
    [displayName],
    [sortOrder],
    [id]
)
VALUES (
    N'Fee',
    N'Fee',
    N'6',
    N'49'
);
INSERT INTO [configuration].[transactionTypes] (
    [code],
    [displayName],
    [sortOrder],
    [id]
)
VALUES (
    N'Other',
    N'Other',
    N'7',
    N'50'
);
INSERT INTO [configuration].[transactionTypes] (
    [code],
    [displayName],
    [sortOrder],
    [id]
)
VALUES (
    N'Refund',
    N'Refund',
    N'8',
    N'51'
);
INSERT INTO [configuration].[transactionTypes] (
    [code],
    [displayName],
    [sortOrder],
    [id]
)
VALUES (
    N'Transfer',
    N'Transfer',
    N'3',
    N'52'
);
INSERT INTO [configuration].[transactionTypes] (
    [code],
    [displayName],
    [sortOrder],
    [id]
)
VALUES (
    N'RefundReversal',
    N'Refund Recall',
    N'9',
    N'53'
);
INSERT INTO [configuration].[transactionTypes] (
    [code],
    [displayName],
    [sortOrder],
    [id]
)
VALUES (
    N'CreditCardPayment',
    N'Credit Card Payment',
    N'10',
    N'54'
);
SET IDENTITY_INSERT [configuration].[transactionTypes] OFF;
GO
INSERT INTO [disputes].[caseStatuses] (
    [code],
    [displayName],
    [sortOrder],
    [customerLabel],
    [tone],
    [stage],
    [nextStep],
    [ownerLabel],
    [steps],
    [revision],
    [updatedAtUtc]
)
VALUES (
    N'AwaitingCustomer',
    N'Awaiting Customer',
    N'2',
    N'Action Needed',
    N'pending',
    N'1',
    N'Your specialist needs a reply from you.',
    N'',
    N'["Received","Your Reply","Complete"]',
    N'1',
    N'2026-11-10T15:14:26.2909888Z'
);
INSERT INTO [disputes].[caseStatuses] (
    [code],
    [displayName],
    [sortOrder],
    [customerLabel],
    [tone],
    [stage],
    [nextStep],
    [ownerLabel],
    [steps],
    [revision],
    [updatedAtUtc]
)
VALUES (
    N'Declined',
    N'Declined',
    N'4',
    N'Declined',
    N'danger',
    N'2',
    N'The review is complete. View the decision below.',
    N'Case Closed',
    N'["Received","In Review","Complete"]',
    N'1',
    N'2026-11-10T15:14:26.3191772Z'
);
INSERT INTO [disputes].[caseStatuses] (
    [code],
    [displayName],
    [sortOrder],
    [customerLabel],
    [tone],
    [stage],
    [nextStep],
    [ownerLabel],
    [steps],
    [revision],
    [updatedAtUtc]
)
VALUES (
    N'Resolved',
    N'Resolved',
    N'3',
    N'Resolved',
    N'success',
    N'2',
    N'Your case is complete. View the outcome below.',
    N'Case Closed',
    N'["Received","In Review","Complete"]',
    N'1',
    N'2026-11-10T15:14:26.3019976Z'
);
INSERT INTO [disputes].[caseStatuses] (
    [code],
    [displayName],
    [sortOrder],
    [customerLabel],
    [tone],
    [stage],
    [nextStep],
    [ownerLabel],
    [steps],
    [revision],
    [updatedAtUtc]
)
VALUES (
    N'Submitted',
    N'Submitted',
    N'0',
    N'Submitted',
    N'pending',
    N'0',
    N'Your case is waiting for review.',
    N'',
    N'["Received","In Review","Complete"]',
    N'1',
    N'2026-11-10T15:14:26.2529771Z'
);
INSERT INTO [disputes].[caseStatuses] (
    [code],
    [displayName],
    [sortOrder],
    [customerLabel],
    [tone],
    [stage],
    [nextStep],
    [ownerLabel],
    [steps],
    [revision],
    [updatedAtUtc]
)
VALUES (
    N'UnderReview',
    N'Under Review',
    N'1',
    N'Under Review',
    N'progress',
    N'1',
    N'Your specialist is reviewing the transaction.',
    N'',
    N'["Received","In Review","Complete"]',
    N'1',
    N'2026-11-10T15:14:26.2749694Z'
);
INSERT INTO [disputes].[caseStatuses] (
    [code],
    [displayName],
    [sortOrder],
    [customerLabel],
    [tone],
    [stage],
    [nextStep],
    [ownerLabel],
    [steps],
    [revision],
    [updatedAtUtc]
)
VALUES (
    N'Withdrawn',
    N'Withdrawn',
    N'5',
    N'Withdrawn',
    N'neutral',
    N'2',
    N'This case has been withdrawn.',
    N'Case Withdrawn',
    N'["Received","In Review","Withdrawn"]',
    N'1',
    N'2026-11-10T15:14:26.3321982Z'
);
GO
SET IDENTITY_INSERT [identity].[permissions] ON;
GO
INSERT INTO [identity].[permissions] (
    [code],
    [id]
)
VALUES (
    N'audit.read',
    N'4'
);
INSERT INTO [identity].[permissions] (
    [code],
    [id]
)
VALUES (
    N'disputes.manage',
    N'5'
);
INSERT INTO [identity].[permissions] (
    [code],
    [id]
)
VALUES (
    N'disputes.read',
    N'6'
);
INSERT INTO [identity].[permissions] (
    [code],
    [id]
)
VALUES (
    N'operations.manage',
    N'7'
);
INSERT INTO [identity].[permissions] (
    [code],
    [id]
)
VALUES (
    N'refund.approve',
    N'55'
);
INSERT INTO [identity].[permissions] (
    [code],
    [id]
)
VALUES (
    N'transactions.assist',
    N'8'
);
INSERT INTO [identity].[permissions] (
    [code],
    [id]
)
VALUES (
    N'users.manage',
    N'9'
);
SET IDENTITY_INSERT [identity].[permissions] OFF;
GO
SET IDENTITY_INSERT [identity].[rolePermissions] ON;
GO
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'25',
    N'4',
    N'35'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'26',
    N'6',
    N'35'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'27',
    N'5',
    N'36'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'28',
    N'6',
    N'36'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'61',
    N'55',
    N'36'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'29',
    N'5',
    N'38'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'30',
    N'6',
    N'38'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'62',
    N'55',
    N'38'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'23',
    N'5',
    N'39'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'24',
    N'6',
    N'39'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'21',
    N'5',
    N'40'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'22',
    N'6',
    N'40'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'63',
    N'55',
    N'40'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'16',
    N'4',
    N'41'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'17',
    N'5',
    N'41'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'18',
    N'6',
    N'41'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'19',
    N'7',
    N'41'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'20',
    N'8',
    N'41'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'64',
    N'55',
    N'41'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'31',
    N'5',
    N'42'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'32',
    N'6',
    N'42'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'33',
    N'6',
    N'43'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'34',
    N'8',
    N'43'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'10',
    N'4',
    N'44'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'11',
    N'5',
    N'44'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'12',
    N'6',
    N'44'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'14',
    N'7',
    N'44'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'15',
    N'8',
    N'44'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'13',
    N'9',
    N'44'
);
INSERT INTO [identity].[rolePermissions] (
    [id],
    [permissionId],
    [roleId]
)
VALUES (
    N'65',
    N'55',
    N'44'
);
SET IDENTITY_INSERT [identity].[rolePermissions] OFF;
GO
SET IDENTITY_INSERT [identity].[roles] ON;
GO
INSERT INTO [identity].[roles] (
    [code],
    [id]
)
VALUES (
    N'audit_reader',
    N'35'
);
INSERT INTO [identity].[roles] (
    [code],
    [id]
)
VALUES (
    N'card_specialist',
    N'36'
);
INSERT INTO [identity].[roles] (
    [code],
    [id]
)
VALUES (
    N'customer',
    N'37'
);
INSERT INTO [identity].[roles] (
    [code],
    [id]
)
VALUES (
    N'debit_order_specialist',
    N'38'
);
INSERT INTO [identity].[roles] (
    [code],
    [id]
)
VALUES (
    N'dispute_agent',
    N'39'
);
INSERT INTO [identity].[roles] (
    [code],
    [id]
)
VALUES (
    N'fraud_agent',
    N'40'
);
INSERT INTO [identity].[roles] (
    [code],
    [id]
)
VALUES (
    N'operations_manager',
    N'41'
);
INSERT INTO [identity].[roles] (
    [code],
    [id]
)
VALUES (
    N'payments_specialist',
    N'42'
);
INSERT INTO [identity].[roles] (
    [code],
    [id]
)
VALUES (
    N'service_clerk',
    N'43'
);
INSERT INTO [identity].[roles] (
    [code],
    [id]
)
VALUES (
    N'super_admin',
    N'44'
);
SET IDENTITY_INSERT [identity].[roles] OFF;
GO
SET IDENTITY_INSERT [support].[concerns] ON;
GO
INSERT INTO [support].[concerns] (
    [id],
    [publicId],
    [code],
    [label],
    [queueType],
    [priority],
    [requiresCase],
    [sortOrder],
    [enabled],
    [contextKind],
    [collectDate]
)
VALUES (
    N'1',
    N'D0A2BBEA-ADF0-5AE9-88D0-27324B48CFCE',
    N'General',
    N'General Enquiry',
    N'General',
    N'2',
    N'0',
    N'1',
    N'1',
    N'None',
    N'0'
);
INSERT INTO [support].[concerns] (
    [id],
    [publicId],
    [code],
    [label],
    [queueType],
    [priority],
    [requiresCase],
    [sortOrder],
    [enabled],
    [contextKind],
    [collectDate]
)
VALUES (
    N'2',
    N'88122291-5ACB-50B5-8CEC-C40A171F5179',
    N'Account',
    N'Account Enquiry',
    N'General',
    N'2',
    N'0',
    N'2',
    N'1',
    N'None',
    N'0'
);
INSERT INTO [support].[concerns] (
    [id],
    [publicId],
    [code],
    [label],
    [queueType],
    [priority],
    [requiresCase],
    [sortOrder],
    [enabled],
    [contextKind],
    [collectDate]
)
VALUES (
    N'3',
    N'B16F0715-9C5B-57C1-9FBA-D6EF6D622D5F',
    N'Payment',
    N'Payment Problem',
    N'Payments',
    N'3',
    N'0',
    N'3',
    N'1',
    N'OptionalTransaction',
    N'0'
);
INSERT INTO [support].[concerns] (
    [id],
    [publicId],
    [code],
    [label],
    [queueType],
    [priority],
    [requiresCase],
    [sortOrder],
    [enabled],
    [contextKind],
    [collectDate]
)
VALUES (
    N'4',
    N'3C8FD44D-5BD0-5D8A-B032-E330E2A39CA2',
    N'Refund',
    N'Refund Enquiry',
    N'General',
    N'3',
    N'1',
    N'4',
    N'1',
    N'Case',
    N'0'
);
INSERT INTO [support].[concerns] (
    [id],
    [publicId],
    [code],
    [label],
    [queueType],
    [priority],
    [requiresCase],
    [sortOrder],
    [enabled],
    [contextKind],
    [collectDate]
)
VALUES (
    N'5',
    N'CD58CFCC-0031-53D2-B7E4-89D9E9194E1B',
    N'Dispute',
    N'Transaction Dispute',
    N'Card',
    N'3',
    N'0',
    N'5',
    N'1',
    N'Transaction',
    N'0'
);
INSERT INTO [support].[concerns] (
    [id],
    [publicId],
    [code],
    [label],
    [queueType],
    [priority],
    [requiresCase],
    [sortOrder],
    [enabled],
    [contextKind],
    [collectDate]
)
VALUES (
    N'6',
    N'4DE477C1-AA51-5F8D-9FB2-57B9E87C53D6',
    N'FollowUp',
    N'Existing Dispute Follow-Up',
    N'General',
    N'2',
    N'1',
    N'6',
    N'1',
    N'Case',
    N'0'
);
INSERT INTO [support].[concerns] (
    [id],
    [publicId],
    [code],
    [label],
    [queueType],
    [priority],
    [requiresCase],
    [sortOrder],
    [enabled],
    [contextKind],
    [collectDate]
)
VALUES (
    N'7',
    N'F18287B0-2652-56B5-9933-DAFCA4CFAEF9',
    N'Unauthorized',
    N'Unauthorized Transaction',
    N'Fraud',
    N'5',
    N'0',
    N'7',
    N'1',
    N'Transaction',
    N'0'
);
INSERT INTO [support].[concerns] (
    [id],
    [publicId],
    [code],
    [label],
    [queueType],
    [priority],
    [requiresCase],
    [sortOrder],
    [enabled],
    [contextKind],
    [collectDate]
)
VALUES (
    N'8',
    N'96679EB7-F00E-5670-91BA-F8B9A857D695',
    N'Fraud',
    N'Fraud Or Suspicious Activity',
    N'Fraud',
    N'5',
    N'0',
    N'8',
    N'1',
    N'OptionalTransaction',
    N'0'
);
INSERT INTO [support].[concerns] (
    [id],
    [publicId],
    [code],
    [label],
    [queueType],
    [priority],
    [requiresCase],
    [sortOrder],
    [enabled],
    [contextKind],
    [collectDate]
)
VALUES (
    N'9',
    N'09A6DF3D-3C3C-5DF7-8548-843279AE12C6',
    N'Technical',
    N'Technical Support',
    N'General',
    N'2',
    N'0',
    N'9',
    N'1',
    N'None',
    N'1'
);
INSERT INTO [support].[concerns] (
    [id],
    [publicId],
    [code],
    [label],
    [queueType],
    [priority],
    [requiresCase],
    [sortOrder],
    [enabled],
    [contextKind],
    [collectDate]
)
VALUES (
    N'10',
    N'8C4E2753-0CE6-5F3B-9F94-06CAB27DC1AB',
    N'Other',
    N'Other',
    N'General',
    N'2',
    N'0',
    N'10',
    N'1',
    N'None',
    N'0'
);
SET IDENTITY_INSERT [support].[concerns] OFF;
GO
ALTER TABLE [accounts].[accounts]
    ADD CONSTRAINT [ck_accounts_11] CHECK ([accountStatus]='Blocked' OR [accountStatus]='Closed' OR [accountStatus]='Active');
GO
ALTER TABLE [accounts].[accounts]
    ADD CONSTRAINT [ck_accounts_12] CHECK (len([currencyCode])=(3) AND NOT [currencyCode] like '%[^A-Z]%');
GO
ALTER TABLE [accounts].[accounts]
    ADD CONSTRAINT [ck_accounts_13] CHECK ([deletedAt] IS NULL OR [accountStatus]='Closed');
GO
ALTER TABLE [accounts].[accounts]
    ADD CONSTRAINT [ck_accounts_countryPolicyVersion] CHECK ([countryPolicyVersion]>=(0));
GO
ALTER TABLE [accounts].[accounts]
    ADD CONSTRAINT [ck_accounts_removalActor] CHECK ([deletedAt] IS NULL AND [deletedById] IS NULL OR [deletedAt] IS NOT NULL AND [deletedById] IS NOT NULL AND datepart(tzoffset,[deletedAt])=(0));
GO
ALTER TABLE [accounts].[accounts]
    ADD CONSTRAINT [fk_accounts_deletedById]
    FOREIGN KEY ([deletedById])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [accounts].[accounts]
    ADD CONSTRAINT [fk_accounts_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_accounts_22]
    ON [accounts].[accounts] ([userId] ASC, [accountStatus] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_accounts_23]
    ON [accounts].[accounts] ([userId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_accounts_24]
    ON [accounts].[accounts] ([publicId] ASC, [userId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_accounts_25]
    ON [accounts].[accounts] ([numberHash] ASC)
    WHERE ([numberHash] IS NOT NULL);
GO
ALTER TABLE [accounts].[bankLinks]
    ADD CONSTRAINT [fk_bankLinks_accountId]
    FOREIGN KEY ([accountId])
    REFERENCES [accounts].[accounts] ([id]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_bankLinks_13]
    ON [accounts].[bankLinks] ([accountId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_bankLinks_14]
    ON [accounts].[bankLinks] ([accountId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_bankLinks_15]
    ON [accounts].[bankLinks] ([provider] ASC, [externalAccountHash] ASC);
GO
ALTER TABLE [audit].[actorSnapshots]
    ADD CONSTRAINT [ck_actorSnapshots_3] CHECK ([actorKind]='Anonymous' OR [actorKind]='System' OR [actorKind]='User');
GO
ALTER TABLE [audit].[actorSnapshots]
    ADD CONSTRAINT [ck_actorSnapshots_4] CHECK ([provenance]='ErasedProfile' OR [provenance]='LegacyProfile' OR [provenance]='Recorded');
GO
ALTER TABLE [audit].[actorSnapshots]
    ADD CONSTRAINT [ck_actorSnapshots_source] CHECK ([sourceKind]=N'Audit' AND [auditEventId] IS NOT NULL AND [automaticDecisionId] IS NULL AND [evidenceId] IS NULL AND [historyId] IS NULL AND [messageId] IS NULL AND [noteId] IS NULL AND [securityEventId] IS NULL OR [sourceKind]=N'Automatic' AND [auditEventId] IS NULL AND [automaticDecisionId] IS NOT NULL AND [evidenceId] IS NULL AND [historyId] IS NULL AND [messageId] IS NULL AND [noteId] IS NULL AND [securityEventId] IS NULL OR [sourceKind]=N'Evidence' AND [auditEventId] IS NULL AND [automaticDecisionId] IS NULL AND [evidenceId] IS NOT NULL AND [historyId] IS NULL AND [messageId] IS NULL AND [noteId] IS NULL AND [securityEventId] IS NULL OR [sourceKind]=N'History' AND [auditEventId] IS NULL AND [automaticDecisionId] IS NULL AND [evidenceId] IS NULL AND [historyId] IS NOT NULL AND [messageId] IS NULL AND [noteId] IS NULL AND [securityEventId] IS NULL OR [sourceKind]=N'Message' AND [auditEventId] IS NULL AND [automaticDecisionId] IS NULL AND [evidenceId] IS NULL AND [historyId] IS NULL AND [messageId] IS NOT NULL AND [noteId] IS NULL AND [securityEventId] IS NULL OR [sourceKind]=N'Note' AND [auditEventId] IS NULL AND [automaticDecisionId] IS NULL AND [evidenceId] IS NULL AND [historyId] IS NULL AND [messageId] IS NULL AND [noteId] IS NOT NULL AND [securityEventId] IS NULL OR [sourceKind]=N'Security' AND [auditEventId] IS NULL AND [automaticDecisionId] IS NULL AND [evidenceId] IS NULL AND [historyId] IS NULL AND [messageId] IS NULL AND [noteId] IS NULL AND [securityEventId] IS NOT NULL);
GO
ALTER TABLE [audit].[actorSnapshots]
    ADD CONSTRAINT [fk_actorSnapshots_actorUserId]
    FOREIGN KEY ([actorUserId])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [audit].[actorSnapshots]
    ADD CONSTRAINT [fk_actorSnapshots_auditEventId]
    FOREIGN KEY ([auditEventId])
    REFERENCES [audit].[auditEvents] ([id]);
GO
ALTER TABLE [audit].[actorSnapshots]
    ADD CONSTRAINT [fk_actorSnapshots_automaticDecisionId]
    FOREIGN KEY ([automaticDecisionId])
    REFERENCES [disputes].[automaticDecisions] ([id]);
GO
ALTER TABLE [audit].[actorSnapshots]
    ADD CONSTRAINT [fk_actorSnapshots_evidenceId]
    FOREIGN KEY ([evidenceId])
    REFERENCES [disputes].[evidence] ([id]);
GO
ALTER TABLE [audit].[actorSnapshots]
    ADD CONSTRAINT [fk_actorSnapshots_historyId]
    FOREIGN KEY ([historyId])
    REFERENCES [disputes].[statusHistory] ([id]);
GO
ALTER TABLE [audit].[actorSnapshots]
    ADD CONSTRAINT [fk_actorSnapshots_messageId]
    FOREIGN KEY ([messageId])
    REFERENCES [support].[messages] ([id]);
GO
ALTER TABLE [audit].[actorSnapshots]
    ADD CONSTRAINT [fk_actorSnapshots_noteId]
    FOREIGN KEY ([noteId])
    REFERENCES [disputes].[notes] ([id]);
GO
ALTER TABLE [audit].[actorSnapshots]
    ADD CONSTRAINT [fk_actorSnapshots_securityEventId]
    FOREIGN KEY ([securityEventId])
    REFERENCES [identity].[securityEvents] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_actorSnapshots_7]
    ON [audit].[actorSnapshots] ([actorUserId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [uq_actorSnapshots_Audit]
    ON [audit].[actorSnapshots] ([auditEventId] ASC)
    WHERE ([auditEventId] IS NOT NULL);
GO
CREATE UNIQUE NONCLUSTERED INDEX [uq_actorSnapshots_Automatic]
    ON [audit].[actorSnapshots] ([automaticDecisionId] ASC)
    WHERE ([automaticDecisionId] IS NOT NULL);
GO
CREATE UNIQUE NONCLUSTERED INDEX [uq_actorSnapshots_Evidence]
    ON [audit].[actorSnapshots] ([evidenceId] ASC)
    WHERE ([evidenceId] IS NOT NULL);
GO
CREATE UNIQUE NONCLUSTERED INDEX [uq_actorSnapshots_History]
    ON [audit].[actorSnapshots] ([historyId] ASC)
    WHERE ([historyId] IS NOT NULL);
GO
CREATE UNIQUE NONCLUSTERED INDEX [uq_actorSnapshots_Message]
    ON [audit].[actorSnapshots] ([messageId] ASC)
    WHERE ([messageId] IS NOT NULL);
GO
CREATE UNIQUE NONCLUSTERED INDEX [uq_actorSnapshots_Note]
    ON [audit].[actorSnapshots] ([noteId] ASC)
    WHERE ([noteId] IS NOT NULL);
GO
CREATE UNIQUE NONCLUSTERED INDEX [uq_actorSnapshots_Security]
    ON [audit].[actorSnapshots] ([securityEventId] ASC)
    WHERE ([securityEventId] IS NOT NULL);
GO
ALTER TABLE [audit].[auditEvents]
    ADD CONSTRAINT [ck_auditEvents_5] CHECK (isjson([safeMetadata])=(1));
GO
ALTER TABLE [audit].[auditEvents]
    ADD CONSTRAINT [fk_auditEvents_actorUserId]
    FOREIGN KEY ([actorUserId])
    REFERENCES [identity].[users] ([id]) ON DELETE SET NULL;
GO
CREATE NONCLUSTERED INDEX [ix_auditEvents_8]
    ON [audit].[auditEvents] ([actorUserId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_auditEvents_9]
    ON [audit].[auditEvents] ([entityType] ASC, [entityPublicId] ASC, [occurredAt] DESC);
GO
CREATE NONCLUSTERED INDEX [ix_auditEvents_10]
    ON [audit].[auditEvents] ([occurredAt] DESC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_auditEvents_recordSoftDeleted]
    ON [audit].[auditEvents] ([entityType] ASC, [entityPublicId] ASC)
    WHERE ([action]='RecordSoftDeleted');
GO
ALTER TABLE [configuration].[disputeCategories]
    ADD CONSTRAINT [ck_disputeCategories_18] CHECK ([sortOrder]>=(0));
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_disputeCategories_41]
    ON [configuration].[disputeCategories] ([code] ASC);
GO
ALTER TABLE [configuration].[disputeCategoryTransactionTypes]
    ADD CONSTRAINT [fk_disputeCategoryTransactionTypes_categoryId]
    FOREIGN KEY ([categoryId])
    REFERENCES [configuration].[disputeCategories] ([id]);
GO
ALTER TABLE [configuration].[disputeCategoryTransactionTypes]
    ADD CONSTRAINT [fk_disputeCategoryTransactionTypes_transactionTypeId]
    FOREIGN KEY ([transactionTypeId])
    REFERENCES [configuration].[transactionTypes] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_disputeCategoryTransactionTypes_transactionTypeId]
    ON [configuration].[disputeCategoryTransactionTypes] ([transactionTypeId] ASC, [categoryId] ASC);
GO
ALTER TABLE [configuration].[disputeInputPolicies]
    ADD CONSTRAINT [CK__dispute_i__reaso__43C1049E] CHECK ([reasonMaximumLength]>=(1) AND [reasonMaximumLength]<=(1000));
GO
ALTER TABLE [configuration].[disputeInputPolicies]
    ADD CONSTRAINT [CK__dispute_i__descr__44B528D7] CHECK ([descriptionMinimumLength]>=(10));
GO
ALTER TABLE [configuration].[disputeInputPolicies]
    ADD CONSTRAINT [CK__dispute_i__descr__45A94D10] CHECK ([descriptionMaximumLength]>=(10) AND [descriptionMaximumLength]<=(2000));
GO
ALTER TABLE [configuration].[disputeInputPolicies]
    ADD CONSTRAINT [CK__dispute_i__comme__469D7149] CHECK ([commentMaximumLength]>=(1) AND [commentMaximumLength]<=(2000));
GO
ALTER TABLE [configuration].[disputeInputPolicies]
    ADD CONSTRAINT [CK__dispute_i__note___47919582] CHECK ([noteMinimumLength]>=(1));
GO
ALTER TABLE [configuration].[disputeInputPolicies]
    ADD CONSTRAINT [CK__dispute_i__note___4885B9BB] CHECK ([noteMaximumLength]>=(1) AND [noteMaximumLength]<=(4000));
GO
ALTER TABLE [configuration].[disputeInputPolicies]
    ADD CONSTRAINT [CK__dispute_i__statu__4979DDF4] CHECK ([statusMaximumLength]>=(1) AND [statusMaximumLength]<=(32));
GO
ALTER TABLE [configuration].[disputeInputPolicies]
    ADD CONSTRAINT [CK__dispute_i__idemp__4A6E022D] CHECK ([idempotencyKeyMinimumLength]>=(8));
GO
ALTER TABLE [configuration].[disputeInputPolicies]
    ADD CONSTRAINT [CK__dispute_i__idemp__4B622666] CHECK ([idempotencyKeyMaximumLength]>=(8) AND [idempotencyKeyMaximumLength]<=(128));
GO
ALTER TABLE [configuration].[disputeInputPolicies]
    ADD CONSTRAINT [CK__dispute_input_po__4C564A9F] CHECK ([descriptionMinimumLength]<=[descriptionMaximumLength]);
GO
ALTER TABLE [configuration].[disputeInputPolicies]
    ADD CONSTRAINT [CK__dispute_input_po__4D4A6ED8] CHECK ([noteMinimumLength]<=[noteMaximumLength]);
GO
ALTER TABLE [configuration].[disputeInputPolicies]
    ADD CONSTRAINT [CK__dispute_input_po__4E3E9311] CHECK ([idempotencyKeyMinimumLength]<=[idempotencyKeyMaximumLength]);
GO
ALTER TABLE [configuration].[evidenceCategories]
    ADD CONSTRAINT [FK__evidenceC__delet__7ECCBBD3]
    FOREIGN KEY ([deletedById])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [configuration].[evidenceCategories]
    ADD CONSTRAINT [FK__evidenceC__setti__7DD8979A]
    FOREIGN KEY ([settingsId])
    REFERENCES [configuration].[portalSettings] ([id]);
GO
ALTER TABLE [configuration].[evidencePolicies]
    ADD CONSTRAINT [CK__evidenceP__maxim__0856260D] CHECK ([maximumFileBytes]>(0));
GO
ALTER TABLE [configuration].[evidencePolicies]
    ADD CONSTRAINT [CK__evidenceP__maxim__094A4A46] CHECK ([maximumFiles]>(0));
GO
ALTER TABLE [configuration].[evidencePolicies]
    ADD CONSTRAINT [CK__evidenceP__maxim__0A3E6E7F] CHECK ([maximumCaseBytes]>(0));
GO
ALTER TABLE [configuration].[evidencePolicies]
    ADD CONSTRAINT [CK__evidenceP__maxim__0B3292B8] CHECK ([maximumFileNameLength]>(0));
GO
ALTER TABLE [configuration].[evidencePolicies]
    ADD CONSTRAINT [CK__evidenceP__minim__0C26B6F1] CHECK ([minimumAccessHours]>(0));
GO
ALTER TABLE [configuration].[evidencePolicies]
    ADD CONSTRAINT [CK__evidenceP__acces__0D1ADB2A] CHECK ([accessReasonMinimumLength]>(0));
GO
ALTER TABLE [configuration].[evidencePolicies]
    ADD CONSTRAINT [CK__evidencePolicies__0E0EFF63] CHECK ([maximumOriginalBytes]>=[maximumFileBytes]);
GO
ALTER TABLE [configuration].[evidencePolicies]
    ADD CONSTRAINT [CK__evidencePolicies__0F03239C] CHECK ([maximumRequestBytes]>([maximumOriginalBytes]+[maximumFileBytes]));
GO
ALTER TABLE [configuration].[evidencePolicies]
    ADD CONSTRAINT [CK__evidencePolicies__0FF747D5] CHECK ([defaultAccessHours]>=[minimumAccessHours] AND [defaultAccessHours]<=[maximumAccessHours]);
GO
ALTER TABLE [configuration].[evidencePolicies]
    ADD CONSTRAINT [CK__evidencePolicies__10EB6C0E] CHECK ([accessReasonMaximumLength]>=[accessReasonMinimumLength]);
GO
ALTER TABLE [configuration].[evidencePolicies]
    ADD CONSTRAINT [FK__evidenceP__setti__076201D4]
    FOREIGN KEY ([settingsId])
    REFERENCES [configuration].[portalSettings] ([id]);
GO
ALTER TABLE [configuration].[portalSettings]
    ADD CONSTRAINT [CK__portalSet__versi__52EE3995] CHECK ([version]>(0));
GO
ALTER TABLE [configuration].[portalSettings]
    ADD CONSTRAINT [CK__portalSett__body__53E25DCE] CHECK (isjson([body])=(1));
GO
ALTER TABLE [configuration].[portalSettings]
    ADD CONSTRAINT [FK__portalSet__creat__51FA155C]
    FOREIGN KEY ([createdById])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [configuration].[refundPolicies]
    ADD CONSTRAINT [ck_refundPolicies_window] CHECK ([effectiveToUtc] IS NULL OR [effectiveToUtc]>[effectiveFromUtc]);
GO
ALTER TABLE [configuration].[refundPolicies]
    ADD CONSTRAINT [ck_refundPolicies_initial] CHECK ([version]=(0) AND [code] IS NOT NULL AND [policyId]=[id] AND [publishedAtUtc] IS NULL AND [configurationKey] IS NOT NULL AND [initialVersion] IS NOT NULL AND [lastVersion]>(0) AND [concurrencyToken]>(0) OR [version]>(0) AND [code] IS NULL AND [configurationKey] IS NULL AND [initialVersion] IS NULL AND [lastVersion] IS NULL AND [concurrencyToken] IS NULL);
GO
ALTER TABLE [configuration].[refundPolicies]
    ADD CONSTRAINT [ck_refundPolicies_removal] CHECK ([isDeleted]=(1) AND [deletedAtUtc] IS NOT NULL AND [deletedById] IS NOT NULL AND [deletionReason] IS NOT NULL OR [isDeleted]=(0) AND [deletedAtUtc] IS NULL AND [deletedById] IS NULL AND [deletionReason] IS NULL);
GO
ALTER TABLE [configuration].[refundPolicies]
    ADD CONSTRAINT [ck_refundPolicies_business] CHECK ([version]>=(0) AND ([priority]>=(-1000) AND [priority]<=(1000)) AND [maximumAmount]>(0) AND [maximumAmount]<=(1000000000000.) AND ([maximumAgeHours]>=(1) AND [maximumAgeHours]<=(876000)) AND [dailyLimit]>=[maximumAmount] AND [dailyLimit]<=(1000000000000.) AND ([duplicateWindowMinutes]>=(1) AND [duplicateWindowMinutes]<=(1440)));
GO
ALTER TABLE [configuration].[refundPolicies]
    ADD CONSTRAINT [FK__refundPol__creat__6AC5C326]
    FOREIGN KEY ([createdById])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [configuration].[refundPolicies]
    ADD CONSTRAINT [FK__refundPol__delet__6BB9E75F]
    FOREIGN KEY ([deletedById])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [configuration].[refundPolicies]
    ADD CONSTRAINT [fk_refundPolicies_family]
    FOREIGN KEY ([policyId])
    REFERENCES [configuration].[refundPolicies] ([id]);
GO
ALTER TABLE [configuration].[refundPolicies]
    ADD CONSTRAINT [fk_refundPolicies_supersedes]
    FOREIGN KEY ([supersedesVersionId])
    REFERENCES [configuration].[refundPolicies] ([id]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [uq_refundPolicies_code]
    ON [configuration].[refundPolicies] ([code] ASC)
    WHERE ([code] IS NOT NULL);
GO
CREATE UNIQUE NONCLUSTERED INDEX [uq_refundPolicies_request]
    ON [configuration].[refundPolicies] ([policyId] ASC, [requestId] ASC)
    WHERE ([requestId] IS NOT NULL);
GO
CREATE NONCLUSTERED INDEX [ix_refundPolicies_effective]
    ON [configuration].[refundPolicies] ([policyId] ASC, [version] ASC, [effectiveFromUtc] ASC);
GO
ALTER TABLE [configuration].[riskPolicies]
    ADD CONSTRAINT [ck_riskPolicies_window] CHECK ([effectiveToUtc] IS NULL OR [effectiveToUtc]>[effectiveFromUtc]);
GO
ALTER TABLE [configuration].[riskPolicies]
    ADD CONSTRAINT [ck_riskPolicies_initial] CHECK ([version]=(0) AND [code] IS NOT NULL AND [policyId]=[id] AND [publishedAtUtc] IS NULL AND [configurationKey] IS NOT NULL AND [initialVersion] IS NOT NULL AND [lastVersion]>(0) AND [concurrencyToken]>(0) OR [version]>(0) AND [code] IS NULL AND [configurationKey] IS NULL AND [initialVersion] IS NULL AND [lastVersion] IS NULL AND [concurrencyToken] IS NULL);
GO
ALTER TABLE [configuration].[riskPolicies]
    ADD CONSTRAINT [ck_riskPolicies_removal] CHECK ([isDeleted]=(1) AND [deletedAtUtc] IS NOT NULL AND [deletedById] IS NOT NULL AND [deletionReason] IS NOT NULL OR [isDeleted]=(0) AND [deletedAtUtc] IS NULL AND [deletedById] IS NULL AND [deletionReason] IS NULL);
GO
ALTER TABLE [configuration].[riskPolicies]
    ADD CONSTRAINT [ck_riskPolicies_business] CHECK ([version]>=(0) AND ([priority]>=(-1000) AND [priority]<=(1000)) AND [highValueThreshold]>(0) AND [highValueThreshold]<=(1000000000000.) AND ([extraCheckAfterDays]>=(0) AND [extraCheckAfterDays]<=(36500)) AND ([debitOrderWindowDays]>=(1) AND [debitOrderWindowDays]<=(36500)) AND [debitOrderAppLimit]>(0) AND [debitOrderAppLimit]<=(1000000000000.));
GO
ALTER TABLE [configuration].[riskPolicies]
    ADD CONSTRAINT [FK__riskPolic__creat__58A712EB]
    FOREIGN KEY ([createdById])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [configuration].[riskPolicies]
    ADD CONSTRAINT [FK__riskPolic__delet__599B3724]
    FOREIGN KEY ([deletedById])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [configuration].[riskPolicies]
    ADD CONSTRAINT [fk_riskPolicies_family]
    FOREIGN KEY ([policyId])
    REFERENCES [configuration].[riskPolicies] ([id]);
GO
ALTER TABLE [configuration].[riskPolicies]
    ADD CONSTRAINT [fk_riskPolicies_supersedes]
    FOREIGN KEY ([supersedesVersionId])
    REFERENCES [configuration].[riskPolicies] ([id]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [uq_riskPolicies_code]
    ON [configuration].[riskPolicies] ([code] ASC)
    WHERE ([code] IS NOT NULL);
GO
CREATE UNIQUE NONCLUSTERED INDEX [uq_riskPolicies_request]
    ON [configuration].[riskPolicies] ([policyId] ASC, [requestId] ASC)
    WHERE ([requestId] IS NOT NULL);
GO
CREATE NONCLUSTERED INDEX [ix_riskPolicies_effective]
    ON [configuration].[riskPolicies] ([policyId] ASC, [version] ASC, [effectiveFromUtc] ASC);
GO
ALTER TABLE [configuration].[transactionTypes]
    ADD CONSTRAINT [ck_transactionTypes_supportedCode] CHECK ([code]='RefundReversal' OR [code]='Refund' OR [code]='Other' OR [code]='Fee' OR [code]='Deposit' OR [code]='CashWithdrawal' OR [code]='Transfer' OR [code]='DebitOrder' OR [code]='CreditCardPayment' OR [code]='CardPurchase');
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_transactionTypes_147]
    ON [configuration].[transactionTypes] ([code] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_service_outbox_pending]
    ON [dbo].[service_outbox] ([state] ASC, [next_attempt_at] ASC);
GO
ALTER TABLE [disputes].[automaticDecisions]
    ADD CONSTRAINT [ck_automaticDecisions_6] CHECK (isjson([factsJson])=(1));
GO
ALTER TABLE [disputes].[automaticDecisions]
    ADD CONSTRAINT [ck_automaticDecisions_7] CHECK ([outcome]='RefundApproved' OR [outcome]='ManualReview');
GO
ALTER TABLE [disputes].[automaticDecisions]
    ADD CONSTRAINT [FK__automatic__refun__3651FAE7]
    FOREIGN KEY ([refundPolicyVersionId])
    REFERENCES [configuration].[refundPolicies] ([id]);
GO
ALTER TABLE [disputes].[automaticDecisions]
    ADD CONSTRAINT [fk_automaticDecisions_disputeId]
    FOREIGN KEY ([disputeId])
    REFERENCES [disputes].[disputes] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_automaticDecisions_11]
    ON [disputes].[automaticDecisions] ([disputeId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_automaticDecisions_12]
    ON [disputes].[automaticDecisions] ([disputeId] ASC, [policyVersion] ASC);
GO
ALTER TABLE [disputes].[caseStatuses]
    ADD CONSTRAINT [CK__caseStatu__sortO__40CF895A] CHECK ([sortOrder]>=(0) AND [sortOrder]<=(1000));
GO
ALTER TABLE [disputes].[caseStatuses]
    ADD CONSTRAINT [CK__caseStatu__stage__41C3AD93] CHECK ([stage]>=(0) AND [stage]<=(2));
GO
ALTER TABLE [disputes].[caseStatuses]
    ADD CONSTRAINT [CK__caseStatu__revis__43ABF605] CHECK ([revision]>(0));
GO
ALTER TABLE [disputes].[caseStatuses]
    ADD CONSTRAINT [CK__caseStatu__steps__45943E77] CHECK (isjson([steps])=(1));
GO
ALTER TABLE [disputes].[disputes]
    ADD CONSTRAINT [ck_disputes_70] CHECK ([status]='Withdrawn' OR [status]='Declined' OR [status]='Resolved' OR [status]='AwaitingCustomer' OR [status]='UnderReview' OR [status]='Submitted');
GO
ALTER TABLE [disputes].[disputes]
    ADD CONSTRAINT [ck_disputes_71] CHECK ([version]>(0));
GO
ALTER TABLE [disputes].[disputes]
    ADD CONSTRAINT [ck_disputes_72] CHECK ([workflowCycle]>(0));
GO
ALTER TABLE [disputes].[disputes]
    ADD CONSTRAINT [ck_disputes_73] CHECK ([queueType]='Payments' OR [queueType]='DebitOrder' OR [queueType]='Card' OR [queueType]='Fraud' OR [queueType]='General');
GO
ALTER TABLE [disputes].[disputes]
    ADD CONSTRAINT [ck_disputes_74] CHECK ([severity]='Critical' OR [severity]='High' OR [severity]='Medium' OR [severity]='Low');
GO
ALTER TABLE [disputes].[disputes]
    ADD CONSTRAINT [ck_disputes_68] CHECK (len([description])>=(10) AND len([description])<=(2000));
GO
ALTER TABLE [disputes].[disputes]
    ADD CONSTRAINT [ck_disputes_69] CHECK ([priority]>=(1) AND [priority]<=(5));
GO
ALTER TABLE [disputes].[disputes]
    ADD CONSTRAINT [FK__disputes__riskPo__3A228BCB]
    FOREIGN KEY ([riskPolicyVersionId])
    REFERENCES [configuration].[riskPolicies] ([id]);
GO
ALTER TABLE [disputes].[disputes]
    ADD CONSTRAINT [fk_cases_status_catalogue]
    FOREIGN KEY ([status])
    REFERENCES [disputes].[caseStatuses] ([code]);
GO
ALTER TABLE [disputes].[disputes]
    ADD CONSTRAINT [fk_disputes_assignedToId]
    FOREIGN KEY ([assignedToId])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [disputes].[disputes]
    ADD CONSTRAINT [fk_disputes_categoryId]
    FOREIGN KEY ([categoryId])
    REFERENCES [configuration].[disputeCategories] ([id]);
GO
ALTER TABLE [disputes].[disputes]
    ADD CONSTRAINT [fk_disputes_customerId]
    FOREIGN KEY ([customerId])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [disputes].[disputes]
    ADD CONSTRAINT [fk_disputes_submittedById]
    FOREIGN KEY ([submittedById])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [disputes].[disputes]
    ADD CONSTRAINT [fk_disputes_transactionId]
    FOREIGN KEY ([transactionId])
    REFERENCES [transactions].[transactions] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_disputes_133]
    ON [disputes].[disputes] ([status] ASC, [queueType] ASC, [assignedToId] ASC, [priority] ASC, [dueAt] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_disputes_134]
    ON [disputes].[disputes] ([customerId] ASC, [createdAt] DESC);
GO
CREATE NONCLUSTERED INDEX [ix_disputes_135]
    ON [disputes].[disputes] ([status] ASC, [priority] DESC, [createdAt] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_disputes_136]
    ON [disputes].[disputes] ([assignedToId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_disputes_137]
    ON [disputes].[disputes] ([customerId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_disputes_138]
    ON [disputes].[disputes] ([submittedById] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_disputes_139]
    ON [disputes].[disputes] ([transactionId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_disputes_140]
    ON [disputes].[disputes] ([publicId] ASC, [customerId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_disputes_141]
    ON [disputes].[disputes] ([reference] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_disputes_142]
    ON [disputes].[disputes] ([transactionId] ASC)
    WHERE ([transactionId] IS NOT NULL AND ([status] IN ('Submitted', 'UnderReview', 'AwaitingCustomer')));
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_disputes_143]
    ON [disputes].[disputes] ([customerId] ASC, [idempotencyKey] ASC)
    WHERE ([idempotencyKey] IS NOT NULL);
GO
ALTER TABLE [disputes].[evidence]
    ADD CONSTRAINT [ck_evidence_19] CHECK ([originalSizeBytes]>=(0) AND [originalSizeBytes]<=(20971520));
GO
ALTER TABLE [disputes].[evidence]
    ADD CONSTRAINT [ck_evidence_20] CHECK ([sizeBytes]>(0) AND [sizeBytes]<=(5242880));
GO
ALTER TABLE [disputes].[evidence]
    ADD CONSTRAINT [ck_evidence_21] CHECK ([category]='Other' OR [category]='ProofOfDelivery' OR [category]='MandateOrAgreement' OR [category]='FraudReport' OR [category]='PhotoOrScreenshot' OR [category]='CancellationConfirmation' OR [category]='MerchantCorrespondence' OR [category]='ReceiptOrInvoice' OR [category]='BankStatement' OR [category]='ProofOfPayment' OR [category]='PaidUpLetter' OR [category]='ProofOfAddress' OR [category]='IdentityDocument');
GO
ALTER TABLE [disputes].[evidence]
    ADD CONSTRAINT [ck_evidence_22] CHECK ([protectedOriginal]<>'binary:v1' OR [payloadId] IS NOT NULL);
GO
ALTER TABLE [disputes].[evidence]
    ADD CONSTRAINT [ck_evidence_23] CHECK ([protectedContent]='binary:v1' AND [payloadId] IS NOT NULL OR [protectedContent]<>'binary:v1' AND [payloadId] IS NULL);
GO
ALTER TABLE [disputes].[evidence]
    ADD CONSTRAINT [ck_evidence_24] CHECK ([scanStatus]='Unscanned' OR [scanStatus]='Scanned' OR [scanStatus]='Validated');
GO
ALTER TABLE [disputes].[evidence]
    ADD CONSTRAINT [ck_evidence_25] CHECK ([originalSizeBytes]=(0) AND [protectedOriginal] IS NULL AND [originalSha256] IS NULL OR [originalSizeBytes]>(0) AND [protectedOriginal] IS NOT NULL AND [originalSha256] IS NOT NULL AND [originalFileName] IS NOT NULL AND [originalContentType] IS NOT NULL);
GO
ALTER TABLE [disputes].[evidence]
    ADD CONSTRAINT [ck_evidence_removalMetadata] CHECK ([deletedAt] IS NULL AND [deletedById] IS NULL AND [deletionReasonProtected] IS NULL OR [deletedAt] IS NOT NULL AND [deletedById] IS NOT NULL AND [deletionReasonProtected] IS NOT NULL AND len([deletionReasonProtected])>(0) AND datepart(tzoffset,[deletedAt])=(0));
GO
ALTER TABLE [disputes].[evidence]
    ADD CONSTRAINT [fk_evidence_category]
    FOREIGN KEY ([categoryId])
    REFERENCES [configuration].[evidenceCategories] ([id]);
GO
ALTER TABLE [disputes].[evidence]
    ADD CONSTRAINT [fk_evidence_deletedById]
    FOREIGN KEY ([deletedById])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [disputes].[evidence]
    ADD CONSTRAINT [fk_evidence_disputeId]
    FOREIGN KEY ([disputeId])
    REFERENCES [disputes].[disputes] ([id]);
GO
ALTER TABLE [disputes].[evidence]
    ADD CONSTRAINT [fk_evidence_payloadId]
    FOREIGN KEY ([payloadId])
    REFERENCES [disputes].[evidencePayloads] ([id]);
GO
ALTER TABLE [disputes].[evidence]
    ADD CONSTRAINT [fk_evidence_uploadedById]
    FOREIGN KEY ([uploadedById])
    REFERENCES [identity].[users] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_evidence_42]
    ON [disputes].[evidence] ([disputeId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_evidence_43]
    ON [disputes].[evidence] ([payloadId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_evidence_44]
    ON [disputes].[evidence] ([uploadedById] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_evidence_45]
    ON [disputes].[evidence] ([disputeId] ASC, [createdAt] ASC, [publicId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_evidence_46]
    ON [disputes].[evidence] ([disputeId] ASC, [sha256] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_evidence_47]
    ON [disputes].[evidence] ([disputeId] ASC, [originalSha256] ASC)
    WHERE ([originalSha256] IS NOT NULL);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_evidence_48]
    ON [disputes].[evidence] ([payloadId] ASC)
    WHERE ([payloadId] IS NOT NULL);
GO
ALTER TABLE [disputes].[evidenceAccessGrants]
    ADD CONSTRAINT [ck_evidenceAccessGrants_40] CHECK ([expiresAt]>[createdAt]);
GO
ALTER TABLE [disputes].[evidenceAccessGrants]
    ADD CONSTRAINT [ck_evidenceAccessGrants_41] CHECK ([userId]<>[grantedById]);
GO
ALTER TABLE [disputes].[evidenceAccessGrants]
    ADD CONSTRAINT [fk_evidenceAccessGrants_evidenceId]
    FOREIGN KEY ([evidenceId])
    REFERENCES [disputes].[evidence] ([id]);
GO
ALTER TABLE [disputes].[evidenceAccessGrants]
    ADD CONSTRAINT [fk_evidenceAccessGrants_grantedById]
    FOREIGN KEY ([grantedById])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [disputes].[evidenceAccessGrants]
    ADD CONSTRAINT [fk_evidenceAccessGrants_revokedById]
    FOREIGN KEY ([revokedById])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [disputes].[evidenceAccessGrants]
    ADD CONSTRAINT [fk_evidenceAccessGrants_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_evidenceAccessGrants_80]
    ON [disputes].[evidenceAccessGrants] ([evidenceId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_evidenceAccessGrants_81]
    ON [disputes].[evidenceAccessGrants] ([grantedById] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_evidenceAccessGrants_82]
    ON [disputes].[evidenceAccessGrants] ([revokedById] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_evidenceAccessGrants_83]
    ON [disputes].[evidenceAccessGrants] ([userId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_evidenceAccessGrants_84]
    ON [disputes].[evidenceAccessGrants] ([evidenceId] ASC, [userId] ASC, [expiresAt] ASC);
GO
ALTER TABLE [disputes].[financialCommands]
    ADD CONSTRAINT [ck_financialCommands_26] CHECK ([state]='Failed' OR [state]='Posted' OR [state]='Finalizing' OR [state]='Queued');
GO
ALTER TABLE [disputes].[financialCommands]
    ADD CONSTRAINT [ck_financialCommands_27] CHECK ([kind]='Recall' OR [kind]='Refund');
GO
ALTER TABLE [disputes].[financialCommands]
    ADD CONSTRAINT [FK__financial__refun__37461F20]
    FOREIGN KEY ([refundPolicyVersionId])
    REFERENCES [configuration].[refundPolicies] ([id]);
GO
ALTER TABLE [disputes].[financialCommands]
    ADD CONSTRAINT [fk_financialCommands_accountId]
    FOREIGN KEY ([accountId])
    REFERENCES [accounts].[accounts] ([id]);
GO
ALTER TABLE [disputes].[financialCommands]
    ADD CONSTRAINT [fk_financialCommands_actorId]
    FOREIGN KEY ([actorId])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [disputes].[financialCommands]
    ADD CONSTRAINT [fk_financialCommands_customerId]
    FOREIGN KEY ([customerId])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [disputes].[financialCommands]
    ADD CONSTRAINT [fk_financialCommands_disputeId]
    FOREIGN KEY ([disputeId])
    REFERENCES [disputes].[disputes] ([id]);
GO
ALTER TABLE [disputes].[financialCommands]
    ADD CONSTRAINT [fk_financialCommands_originalTransactionId]
    FOREIGN KEY ([originalTransactionId])
    REFERENCES [transactions].[transactions] ([id]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_financialCommands_49]
    ON [disputes].[financialCommands] ([disputeId] ASC, [kind] ASC)
    WHERE ([state]='Queued');
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_financialCommands_50]
    ON [disputes].[financialCommands] ([requestId] ASC);
GO
ALTER TABLE [disputes].[notes]
    ADD CONSTRAINT [ck_notes_28] CHECK (len([content])>=(1) AND len([content])<=(4000));
GO
ALTER TABLE [disputes].[notes]
    ADD CONSTRAINT [ck_notes_29] CHECK ([visibility]='Internal' OR [visibility]='Customer');
GO
ALTER TABLE [disputes].[notes]
    ADD CONSTRAINT [fk_notes_authorUserId]
    FOREIGN KEY ([authorUserId])
    REFERENCES [identity].[users] ([id]) ON DELETE SET NULL;
GO
ALTER TABLE [disputes].[notes]
    ADD CONSTRAINT [fk_notes_disputeId]
    FOREIGN KEY ([disputeId])
    REFERENCES [disputes].[disputes] ([id]) ON DELETE CASCADE;
GO
CREATE NONCLUSTERED INDEX [ix_notes_51]
    ON [disputes].[notes] ([authorUserId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_notes_52]
    ON [disputes].[notes] ([disputeId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_notes_53]
    ON [disputes].[notes] ([disputeId] ASC, [visibility] ASC, [createdAt] ASC);
GO
ALTER TABLE [disputes].[refundRecalls]
    ADD CONSTRAINT [ck_refundRecalls_30] CHECK ([amount]>(0));
GO
ALTER TABLE [disputes].[refundRecalls]
    ADD CONSTRAINT [ck_refundRecalls_31] CHECK ([expectedVersion]>(0));
GO
ALTER TABLE [disputes].[refundRecalls]
    ADD CONSTRAINT [ck_refundRecalls_32] CHECK (len(ltrim(rtrim([reason])))>=(10) AND len(ltrim(rtrim([reason])))<=(1000));
GO
ALTER TABLE [disputes].[refundRecalls]
    ADD CONSTRAINT [ck_refundRecalls_33] CHECK ([creditTransactionId]<>[reversalTransactionId]);
GO
ALTER TABLE [disputes].[refundRecalls]
    ADD CONSTRAINT [fk_refundRecalls_creditTransactionId]
    FOREIGN KEY ([creditTransactionId])
    REFERENCES [transactions].[transactions] ([id]);
GO
ALTER TABLE [disputes].[refundRecalls]
    ADD CONSTRAINT [fk_refundRecalls_disputeId]
    FOREIGN KEY ([disputeId])
    REFERENCES [disputes].[disputes] ([id]);
GO
ALTER TABLE [disputes].[refundRecalls]
    ADD CONSTRAINT [fk_refundRecalls_recalledById]
    FOREIGN KEY ([recalledById])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [disputes].[refundRecalls]
    ADD CONSTRAINT [fk_refundRecalls_reversalTransactionId]
    FOREIGN KEY ([reversalTransactionId])
    REFERENCES [transactions].[transactions] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_refundRecalls_54]
    ON [disputes].[refundRecalls] ([recalledById] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_refundRecalls_55]
    ON [disputes].[refundRecalls] ([reversalTransactionId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_refundRecalls_56]
    ON [disputes].[refundRecalls] ([requestId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_refundRecalls_57]
    ON [disputes].[refundRecalls] ([creditTransactionId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_refundRecalls_58]
    ON [disputes].[refundRecalls] ([disputeId] ASC);
GO
ALTER TABLE [disputes].[refundReceipts]
    ADD CONSTRAINT [ck_refundReceipts_54] CHECK ([customerReference] like 'RF-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]');
GO
ALTER TABLE [disputes].[refundReceipts]
    ADD CONSTRAINT [fk_refundReceipts_refundId]
    FOREIGN KEY ([refundId])
    REFERENCES [disputes].[refunds] ([id]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_refundReceipts_107]
    ON [disputes].[refundReceipts] ([refundId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_refundReceipts_108]
    ON [disputes].[refundReceipts] ([refundId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_refundReceipts_109]
    ON [disputes].[refundReceipts] ([customerReference] ASC);
GO
ALTER TABLE [disputes].[refunds]
    ADD CONSTRAINT [ck_refunds_34] CHECK ([amount]>(0));
GO
ALTER TABLE [disputes].[refunds]
    ADD CONSTRAINT [ck_refunds_35] CHECK ([approvalSource]='System' OR [approvalSource]='Staff');
GO
ALTER TABLE [disputes].[refunds]
    ADD CONSTRAINT [FK__refunds__refundP__383A4359]
    FOREIGN KEY ([refundPolicyVersionId])
    REFERENCES [configuration].[refundPolicies] ([id]);
GO
ALTER TABLE [disputes].[refunds]
    ADD CONSTRAINT [fk_refunds_accountId]
    FOREIGN KEY ([accountId])
    REFERENCES [accounts].[accounts] ([id]);
GO
ALTER TABLE [disputes].[refunds]
    ADD CONSTRAINT [fk_refunds_approvedById]
    FOREIGN KEY ([approvedById])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [disputes].[refunds]
    ADD CONSTRAINT [fk_refunds_creditTransactionId]
    FOREIGN KEY ([creditTransactionId])
    REFERENCES [transactions].[transactions] ([id]);
GO
ALTER TABLE [disputes].[refunds]
    ADD CONSTRAINT [fk_refunds_customerId]
    FOREIGN KEY ([customerId])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [disputes].[refunds]
    ADD CONSTRAINT [fk_refunds_disputeId]
    FOREIGN KEY ([disputeId])
    REFERENCES [disputes].[disputes] ([id]);
GO
ALTER TABLE [disputes].[refunds]
    ADD CONSTRAINT [fk_refunds_transactionId]
    FOREIGN KEY ([transactionId])
    REFERENCES [transactions].[transactions] ([id]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_refunds_59]
    ON [disputes].[refunds] ([disputeId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_refunds_60]
    ON [disputes].[refunds] ([accountId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_refunds_61]
    ON [disputes].[refunds] ([approvedById] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_refunds_62]
    ON [disputes].[refunds] ([creditTransactionId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_refunds_63]
    ON [disputes].[refunds] ([customerId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_refunds_64]
    ON [disputes].[refunds] ([disputeId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_refunds_65]
    ON [disputes].[refunds] ([transactionId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_refunds_66]
    ON [disputes].[refunds] ([customerId] ASC, [approvalSource] ASC, [postedAt] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_refunds_67]
    ON [disputes].[refunds] ([requestId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_refunds_68]
    ON [disputes].[refunds] ([creditTransactionId] ASC);
GO
ALTER TABLE [disputes].[reinstatements]
    ADD CONSTRAINT [ck_reinstatements_36] CHECK ([expectedVersion]>(0));
GO
ALTER TABLE [disputes].[reinstatements]
    ADD CONSTRAINT [ck_reinstatements_37] CHECK (len(ltrim(rtrim([reason])))>=(10) AND len(ltrim(rtrim([reason])))<=(1000));
GO
ALTER TABLE [disputes].[reinstatements]
    ADD CONSTRAINT [ck_reinstatements_38] CHECK ([parentDisputeId]<>[childDisputeId]);
GO
ALTER TABLE [disputes].[reinstatements]
    ADD CONSTRAINT [fk_reinstatements_childDisputeId]
    FOREIGN KEY ([childDisputeId])
    REFERENCES [disputes].[disputes] ([id]);
GO
ALTER TABLE [disputes].[reinstatements]
    ADD CONSTRAINT [fk_reinstatements_customerId]
    FOREIGN KEY ([customerId])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [disputes].[reinstatements]
    ADD CONSTRAINT [fk_reinstatements_parentDisputeId]
    FOREIGN KEY ([parentDisputeId])
    REFERENCES [disputes].[disputes] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_reinstatements_69]
    ON [disputes].[reinstatements] ([customerId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_reinstatements_70]
    ON [disputes].[reinstatements] ([requestId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_reinstatements_71]
    ON [disputes].[reinstatements] ([childDisputeId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_reinstatements_72]
    ON [disputes].[reinstatements] ([parentDisputeId] ASC);
GO
ALTER TABLE [disputes].[statusHistory]
    ADD CONSTRAINT [ck_statusHistory_39] CHECK ([newStatus]='Withdrawn' OR [newStatus]='Declined' OR [newStatus]='Resolved' OR [newStatus]='AwaitingCustomer' OR [newStatus]='UnderReview' OR [newStatus]='Submitted');
GO
ALTER TABLE [disputes].[statusHistory]
    ADD CONSTRAINT [fk_history_new_status_catalogue]
    FOREIGN KEY ([newStatus])
    REFERENCES [disputes].[caseStatuses] ([code]);
GO
ALTER TABLE [disputes].[statusHistory]
    ADD CONSTRAINT [fk_history_previous_status_catalogue]
    FOREIGN KEY ([previousStatus])
    REFERENCES [disputes].[caseStatuses] ([code]);
GO
ALTER TABLE [disputes].[statusHistory]
    ADD CONSTRAINT [fk_statusHistory_actorUserId]
    FOREIGN KEY ([actorUserId])
    REFERENCES [identity].[users] ([id]) ON DELETE SET NULL;
GO
ALTER TABLE [disputes].[statusHistory]
    ADD CONSTRAINT [fk_statusHistory_disputeId]
    FOREIGN KEY ([disputeId])
    REFERENCES [disputes].[disputes] ([id]) ON DELETE CASCADE;
GO
CREATE NONCLUSTERED INDEX [ix_statusHistory_73]
    ON [disputes].[statusHistory] ([disputeId] ASC, [changedAt] ASC, [publicId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_statusHistory_74]
    ON [disputes].[statusHistory] ([actorUserId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_statusHistory_75]
    ON [disputes].[statusHistory] ([disputeId] ASC);
GO
ALTER TABLE [identity].[accountDeletionChallenges]
    ADD CONSTRAINT [ck_accountDeletionChallenges_1] CHECK ([state]='Failed' OR [state]='Used' OR [state]='Ready' OR [state]='Pending');
GO
ALTER TABLE [identity].[accountDeletionChallenges]
    ADD CONSTRAINT [fk_accountDeletionChallenges_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_accountDeletionChallenges_1]
    ON [identity].[accountDeletionChallenges] ([userId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_accountDeletionChallenges_2]
    ON [identity].[accountDeletionChallenges] ([userId] ASC, [createdAt] ASC);
GO
ALTER TABLE [identity].[accountDeletionRequests]
    ADD CONSTRAINT [ck_accountDeletionRequests_2] CHECK ([status]='Cancelled' OR [status]='Completed' OR [status]='Declined' OR [status]='UnderReview' OR [status]='Pending');
GO
ALTER TABLE [identity].[accountDeletionRequests]
    ADD CONSTRAINT [fk_accountDeletionRequests_reviewedById]
    FOREIGN KEY ([reviewedById])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [identity].[accountDeletionRequests]
    ADD CONSTRAINT [fk_accountDeletionRequests_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_accountDeletionRequests_3]
    ON [identity].[accountDeletionRequests] ([reviewedById] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_accountDeletionRequests_4]
    ON [identity].[accountDeletionRequests] ([userId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_accountDeletionRequests_5]
    ON [identity].[accountDeletionRequests] ([status] ASC, [createdAt] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_accountDeletionRequests_6]
    ON [identity].[accountDeletionRequests] ([reference] ASC);
GO
ALTER TABLE [identity].[contactVerificationChallenges]
    ADD CONSTRAINT [ck_contactVerificationChallenges_9] CHECK ([purpose]='phone' OR [purpose]='email');
GO
ALTER TABLE [identity].[contactVerificationChallenges]
    ADD CONSTRAINT [ck_contactVerificationChallenges_10] CHECK ([state]='Failed' OR [state]='Used' OR [state]='Ready' OR [state]='Pending');
GO
ALTER TABLE [identity].[contactVerificationChallenges]
    ADD CONSTRAINT [ck_contactVerificationChallenges_8] CHECK ([channel]='whatsapp' OR [channel]='sms' OR [channel]='email');
GO
ALTER TABLE [identity].[contactVerificationChallenges]
    ADD CONSTRAINT [fk_contactVerificationChallenges_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_contactVerificationChallenges_20]
    ON [identity].[contactVerificationChallenges] ([userId] ASC, [createdAt] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_contactVerificationChallenges_21]
    ON [identity].[contactVerificationChallenges] ([userId] ASC);
GO
ALTER TABLE [identity].[customerProfiles]
    ADD CONSTRAINT [ck_customerProfiles_14] CHECK ([protectedIdentityNumber] IS NULL AND [identityLastFour] IS NULL AND [dateOfBirth] IS NULL OR [protectedIdentityNumber] IS NOT NULL AND [identityLastFour] IS NOT NULL AND [dateOfBirth] IS NOT NULL);
GO
ALTER TABLE [identity].[customerProfiles]
    ADD CONSTRAINT [fk_customerProfiles_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]) ON DELETE CASCADE;
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_customerProfiles_26]
    ON [identity].[customerProfiles] ([userId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_customerProfiles_27]
    ON [identity].[customerProfiles] ([userId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_customerProfiles_28]
    ON [identity].[customerProfiles] ([phone] ASC)
    WHERE ([phone] IS NOT NULL AND [contactLoginEnabled]=(1));
GO
ALTER TABLE [identity].[disputeVerifications]
    ADD CONSTRAINT [FK__disputeVe__riskP__392E6792]
    FOREIGN KEY ([riskPolicyVersionId])
    REFERENCES [configuration].[riskPolicies] ([id]);
GO
ALTER TABLE [identity].[disputeVerifications]
    ADD CONSTRAINT [fk_disputeVerifications_sessionId]
    FOREIGN KEY ([sessionId])
    REFERENCES [identity].[userSessions] ([id]);
GO
ALTER TABLE [identity].[disputeVerifications]
    ADD CONSTRAINT [fk_disputeVerifications_transactionId]
    FOREIGN KEY ([transactionId])
    REFERENCES [transactions].[transactions] ([id]);
GO
ALTER TABLE [identity].[disputeVerifications]
    ADD CONSTRAINT [fk_disputeVerifications_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_disputeVerifications_76]
    ON [identity].[disputeVerifications] ([userId] ASC, [transactionId] ASC, [createdAt] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_disputeVerifications_77]
    ON [identity].[disputeVerifications] ([sessionId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_disputeVerifications_78]
    ON [identity].[disputeVerifications] ([transactionId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_disputeVerifications_79]
    ON [identity].[disputeVerifications] ([userId] ASC);
GO
ALTER TABLE [identity].[identityChallenges]
    ADD CONSTRAINT [ck_identityChallenges_44] CHECK ([purpose]='Recovery' OR [purpose]='Login' OR [purpose]='Registration');
GO
ALTER TABLE [identity].[identityChallenges]
    ADD CONSTRAINT [ck_identityChallenges_45] CHECK ([state]='Failed' OR [state]='Used' OR [state]='Ready' OR [state]='Pending');
GO
ALTER TABLE [identity].[identityChallenges]
    ADD CONSTRAINT [fk_identityChallenges_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_identityChallenges_89]
    ON [identity].[identityChallenges] ([userId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_identityChallenges_90]
    ON [identity].[identityChallenges] ([destinationHash] ASC, [purpose] ASC, [createdAt] ASC);
GO
ALTER TABLE [identity].[loginVerificationFlows]
    ADD CONSTRAINT [ckLoginVerificationFlowsResend] CHECK ([resendCount]>=(0) AND [resendCount]<=(5));
GO
ALTER TABLE [identity].[loginVerificationFlows]
    ADD CONSTRAINT [fkLoginVerificationFlowsChallenge]
    FOREIGN KEY ([challengeId])
    REFERENCES [identity].[identityChallenges] ([id]);
GO
ALTER TABLE [identity].[loginVerificationFlows]
    ADD CONSTRAINT [fkLoginVerificationFlowsUser]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE NONCLUSTERED INDEX [ixLoginVerificationFlowsUserCreated]
    ON [identity].[loginVerificationFlows] ([userId] ASC, [createdAt] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_permissions_102]
    ON [identity].[permissions] ([code] ASC);
GO
ALTER TABLE [identity].[rolePermissions]
    ADD CONSTRAINT [fk_rolePermissions_permissionId]
    FOREIGN KEY ([permissionId])
    REFERENCES [identity].[permissions] ([id]) ON DELETE CASCADE;
GO
ALTER TABLE [identity].[rolePermissions]
    ADD CONSTRAINT [fk_rolePermissions_roleId]
    FOREIGN KEY ([roleId])
    REFERENCES [identity].[roles] ([id]) ON DELETE CASCADE;
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_rolePermissions_110]
    ON [identity].[rolePermissions] ([roleId] ASC, [permissionId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_rolePermissions_111]
    ON [identity].[rolePermissions] ([permissionId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_rolePermissions_112]
    ON [identity].[rolePermissions] ([roleId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_roles_113]
    ON [identity].[roles] ([code] ASC);
GO
ALTER TABLE [identity].[securityEvents]
    ADD CONSTRAINT [fk_securityEvents_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_securityEvents_114]
    ON [identity].[securityEvents] ([userId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_securityEvents_115]
    ON [identity].[securityEvents] ([userId] ASC, [eventType] ASC, [outcome] ASC, [occurredAt] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_securityEvents_116]
    ON [identity].[securityEvents] ([userId] ASC, [occurredAt] ASC);
GO
ALTER TABLE [identity].[trustedLoginDevices]
    ADD CONSTRAINT [fk_trustedLoginDevices_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_trustedLoginDevices_154]
    ON [identity].[trustedLoginDevices] ([userId] ASC, [revokedAt] ASC, [expiresAt] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_trustedLoginDevices_155]
    ON [identity].[trustedLoginDevices] ([userId] ASC);
GO
ALTER TABLE [identity].[userRoles]
    ADD CONSTRAINT [fk_userRoles_roleId]
    FOREIGN KEY ([roleId])
    REFERENCES [identity].[roles] ([id]) ON DELETE CASCADE;
GO
ALTER TABLE [identity].[userRoles]
    ADD CONSTRAINT [fk_userRoles_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]) ON DELETE CASCADE;
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_userRoles_156]
    ON [identity].[userRoles] ([userId] ASC, [roleId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_userRoles_157]
    ON [identity].[userRoles] ([roleId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_userRoles_158]
    ON [identity].[userRoles] ([userId] ASC);
GO
ALTER TABLE [identity].[users]
    ADD CONSTRAINT [ck_users_83] CHECK ([accountStatus]='Disabled' OR [accountStatus]='Locked' OR [accountStatus]='Active');
GO
ALTER TABLE [identity].[users]
    ADD CONSTRAINT [ck_users_84] CHECK ([deletedAt] IS NULL OR [accountStatus]='Disabled');
GO
ALTER TABLE [identity].[users]
    ADD CONSTRAINT [ck_users_removalActor] CHECK ([deletedAt] IS NULL AND [deletedById] IS NULL OR [deletedAt] IS NOT NULL AND [deletedById] IS NOT NULL AND datepart(tzoffset,[deletedAt])=(0));
GO
ALTER TABLE [identity].[users]
    ADD CONSTRAINT [fk_users_deletedById]
    FOREIGN KEY ([deletedById])
    REFERENCES [identity].[users] ([id]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_users_161]
    ON [identity].[users] ([normalizedLoginIdentifier] ASC);
GO
ALTER TABLE [identity].[userSessions]
    ADD CONSTRAINT [ck_userSessions_82] CHECK ([expiresAt]>[createdAt]);
GO
ALTER TABLE [identity].[userSessions]
    ADD CONSTRAINT [fk_userSessions_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]) ON DELETE CASCADE;
GO
CREATE NONCLUSTERED INDEX [ix_userSessions_159]
    ON [identity].[userSessions] ([publicId] ASC, [expiresAt] ASC)
    WHERE ([revokedAt] IS NULL);
GO
CREATE NONCLUSTERED INDEX [ix_userSessions_160]
    ON [identity].[userSessions] ([userId] ASC);
GO
ALTER TABLE [integration].[accountMigrations]
    ADD CONSTRAINT [ck_accountMigrations_53] CHECK ([state]='Active' OR [state]='Preparing');
GO
ALTER TABLE [integration].[accountMigrations]
    ADD CONSTRAINT [fk_accountMigrations_accountId]
    FOREIGN KEY ([accountId])
    REFERENCES [accounts].[accounts] ([id]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_accountMigrations_103]
    ON [integration].[accountMigrations] ([accountId] ASC);
GO
ALTER TABLE [integration].[bankingRegenerations]
    ADD CONSTRAINT [ck_bankingRegenerations_46] CHECK ([accountCount]>(0));
GO
ALTER TABLE [integration].[bankingRegenerations]
    ADD CONSTRAINT [ck_bankingRegenerations_47] CHECK ([archivedCount]>=(0));
GO
ALTER TABLE [integration].[bankingRegenerations]
    ADD CONSTRAINT [ck_bankingRegenerations_48] CHECK ([createdCount]>(0));
GO
ALTER TABLE [integration].[bankingRegenerations]
    ADD CONSTRAINT [fk_bankingRegenerations_actorId]
    FOREIGN KEY ([actorId])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [integration].[localBankingAccounts]
    ADD CONSTRAINT [ck_localBankingAccounts_15] CHECK ([initialCount]>=(1) AND [initialCount]<=(50));
GO
ALTER TABLE [integration].[localBankingAccounts]
    ADD CONSTRAINT [fk_localBankingAccounts_accountId]
    FOREIGN KEY ([accountId])
    REFERENCES [accounts].[accounts] ([id]);
GO
ALTER TABLE [integration].[localBankingAccounts]
    ADD CONSTRAINT [fk_localBankingAccounts_bankingIdentityId]
    FOREIGN KEY ([bankingIdentityId])
    REFERENCES [integration].[localBankingIdentities] ([id]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_localBankingAccounts_29]
    ON [integration].[localBankingAccounts] ([accountId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_localBankingAccounts_30]
    ON [integration].[localBankingAccounts] ([accountId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_localBankingAccounts_31]
    ON [integration].[localBankingAccounts] ([bankingIdentityId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_localBankingAccounts_32]
    ON [integration].[localBankingAccounts] ([openingRequestId] ASC);
GO
ALTER TABLE [integration].[localBankingIdentities]
    ADD CONSTRAINT [fk_localBankingIdentities_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_localBankingIdentities_33]
    ON [integration].[localBankingIdentities] ([userId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_localBankingIdentities_34]
    ON [integration].[localBankingIdentities] ([userId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_localBankingIdentities_35]
    ON [integration].[localBankingIdentities] ([identityHash] ASC)
    WHERE ([identityHash] IS NOT NULL);
GO
ALTER TABLE [integration].[localBankingOnboarding]
    ADD CONSTRAINT [fk_localBankingOnboarding_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_localBankingOnboarding_36]
    ON [integration].[localBankingOnboarding] ([userId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_localBankingOnboarding_37]
    ON [integration].[localBankingOnboarding] ([userId] ASC);
GO
ALTER TABLE [integration].[localTransactionBatches]
    ADD CONSTRAINT [ck_localTransactionBatches_16] CHECK ([state]='Failed' OR [state]='Completed' OR [state]='Pending');
GO
ALTER TABLE [integration].[localTransactionBatches]
    ADD CONSTRAINT [ck_localTransactionBatches_17] CHECK ([transactionCount]>=(1) AND [transactionCount]<=(50));
GO
ALTER TABLE [integration].[localTransactionBatches]
    ADD CONSTRAINT [fk_localTransactionBatches_bankingAccountId]
    FOREIGN KEY ([bankingAccountId])
    REFERENCES [integration].[localBankingAccounts] ([id]);
GO
ALTER TABLE [integration].[localTransactionBatches]
    ADD CONSTRAINT [fk_localTransactionBatches_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_localTransactionBatches_38]
    ON [integration].[localTransactionBatches] ([state] ASC, [createdAt] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_localTransactionBatches_39]
    ON [integration].[localTransactionBatches] ([bankingAccountId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_localTransactionBatches_40]
    ON [integration].[localTransactionBatches] ([userId] ASC);
GO
ALTER TABLE [ledger].[projectionEntries]
    ADD CONSTRAINT [ck_projectionEntries_42] CHECK ([eventKind]='Correction' OR [eventKind]='Posting' OR [eventKind]='Initial');
GO
ALTER TABLE [ledger].[projectionEntries]
    ADD CONSTRAINT [ck_projectionEntries_43] CHECK ([unknownDelta]>=(-1) AND [unknownDelta]<=(1));
GO
ALTER TABLE [ledger].[projectionEntries]
    ADD CONSTRAINT [fk_projectionEntries_accountId]
    FOREIGN KEY ([accountId])
    REFERENCES [accounts].[accounts] ([id]);
GO
ALTER TABLE [ledger].[projectionEntries]
    ADD CONSTRAINT [fk_projectionEntries_transactionId]
    FOREIGN KEY ([transactionId])
    REFERENCES [transactions].[transactions] ([id]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_projectionEntries_85]
    ON [ledger].[projectionEntries] ([sequenceNo] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_projectionEntries_86]
    ON [ledger].[projectionEntries] ([accountId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_projectionEntries_87]
    ON [ledger].[projectionEntries] ([transactionId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_projectionEntries_88]
    ON [ledger].[projectionEntries] ([accountId] ASC, [sequenceNo] ASC);
GO
ALTER TABLE [monitoring].[apiMetricSummaries]
    ADD CONSTRAINT [CK__api_metri__reque__3E082B48] CHECK ([requestCount]>(0));
GO
CREATE NONCLUSTERED INDEX [IX_api_metric_batches_retention]
    ON [monitoring].[metricBatches] ([createdAt] ASC);
GO
ALTER TABLE [notifications].[deliveries]
    ADD CONSTRAINT [ck_deliveries_49] CHECK ([workflowCycle]>(0));
GO
ALTER TABLE [notifications].[deliveries]
    ADD CONSTRAINT [fk_deliveries_disputeId]
    FOREIGN KEY ([disputeId])
    REFERENCES [disputes].[disputes] ([id]);
GO
ALTER TABLE [notifications].[deliveries]
    ADD CONSTRAINT [fk_deliveries_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_deliveries_91]
    ON [notifications].[deliveries] ([disputeId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_deliveries_92]
    ON [notifications].[deliveries] ([userId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_deliveries_93]
    ON [notifications].[deliveries] ([state] ASC, [nextAttemptAt] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_deliveries_94]
    ON [notifications].[deliveries] ([userId] ASC, [disputeId] ASC, [workflowCycle] ASC, [milestone] ASC, [channel] ASC);
GO
ALTER TABLE [notifications].[events]
    ADD CONSTRAINT [ck_events_50] CHECK ([attempts]>=(0));
GO
ALTER TABLE [notifications].[events]
    ADD CONSTRAINT [ck_events_51] CHECK ([state]='Review' OR [state]='Skipped' OR [state]='Sent' OR [state]='Processing' OR [state]='Pending');
GO
ALTER TABLE [notifications].[events]
    ADD CONSTRAINT [fk_events_disputeId]
    FOREIGN KEY ([disputeId])
    REFERENCES [disputes].[disputes] ([id]);
GO
ALTER TABLE [notifications].[events]
    ADD CONSTRAINT [fk_events_notificationId]
    FOREIGN KEY ([notificationId])
    REFERENCES [notifications].[notifications] ([id]);
GO
ALTER TABLE [notifications].[events]
    ADD CONSTRAINT [fk_events_supportThreadId]
    FOREIGN KEY ([supportThreadId])
    REFERENCES [support].[conversations] ([id]);
GO
ALTER TABLE [notifications].[events]
    ADD CONSTRAINT [fk_events_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_events_95]
    ON [notifications].[events] ([state] ASC, [nextAttemptAt] ASC, [createdAt] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_events_96]
    ON [notifications].[events] ([userId] ASC, [eventKey] ASC);
GO
ALTER TABLE [notifications].[notifications]
    ADD CONSTRAINT [fk_notifications_disputeId]
    FOREIGN KEY ([disputeId])
    REFERENCES [disputes].[disputes] ([id]) ON DELETE CASCADE;
GO
ALTER TABLE [notifications].[notifications]
    ADD CONSTRAINT [fk_notifications_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]) ON DELETE CASCADE;
GO
CREATE NONCLUSTERED INDEX [ix_notifications_99]
    ON [notifications].[notifications] ([disputeId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_notifications_100]
    ON [notifications].[notifications] ([userId] ASC, [createdAt] DESC);
GO
CREATE NONCLUSTERED INDEX [ix_notifications_101]
    ON [notifications].[notifications] ([userId] ASC);
GO
ALTER TABLE [notifications].[preferences]
    ADD CONSTRAINT [ck_preferences_52] CHECK ([channel]='whatsapp' OR [channel]='sms' OR [channel]='email' OR [channel]='inapp');
GO
ALTER TABLE [notifications].[preferences]
    ADD CONSTRAINT [fk_preferences_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_preferences_97]
    ON [notifications].[preferences] ([userId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_preferences_98]
    ON [notifications].[preferences] ([userId] ASC);
GO
ALTER TABLE [notifications].[pushSubscriptions]
    ADD CONSTRAINT [fk_pushSubscriptions_sessionId]
    FOREIGN KEY ([sessionId])
    REFERENCES [identity].[userSessions] ([id]);
GO
ALTER TABLE [notifications].[pushSubscriptions]
    ADD CONSTRAINT [fk_pushSubscriptions_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_pushSubscriptions_104]
    ON [notifications].[pushSubscriptions] ([sessionId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_pushSubscriptions_105]
    ON [notifications].[pushSubscriptions] ([userId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_pushSubscriptions_106]
    ON [notifications].[pushSubscriptions] ([endpointHash] ASC);
GO
ALTER TABLE [support].[concerns]
    ADD CONSTRAINT [ck_concerns_57] CHECK ([priority]>=(1) AND [priority]<=(5));
GO
ALTER TABLE [support].[concerns]
    ADD CONSTRAINT [ck_concerns_58] CHECK ([queueType]='Payments' OR [queueType]='DebitOrder' OR [queueType]='Card' OR [queueType]='Fraud' OR [queueType]='General');
GO
ALTER TABLE [support].[concerns]
    ADD CONSTRAINT [ck_concerns_59] CHECK ([contextKind]='OptionalTransaction' OR [contextKind]='Transaction' OR [contextKind]='Case' OR [contextKind]='None');
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_concerns_119]
    ON [support].[concerns] ([code] ASC);
GO
ALTER TABLE [support].[conversations]
    ADD CONSTRAINT [ck_conversations_61] CHECK ([conversationStatus]='Closed' OR [conversationStatus]='Open');
GO
ALTER TABLE [support].[conversations]
    ADD CONSTRAINT [ck_conversations_62] CHECK ([escalationLevel]>=(0) AND [escalationLevel]<=(3));
GO
ALTER TABLE [support].[conversations]
    ADD CONSTRAINT [ck_conversations_63] CHECK ([priority]>=(1) AND [priority]<=(5));
GO
ALTER TABLE [support].[conversations]
    ADD CONSTRAINT [ck_conversations_64] CHECK ([queueType]='Payments' OR [queueType]='DebitOrder' OR [queueType]='Card' OR [queueType]='Fraud' OR [queueType]='General');
GO
ALTER TABLE [support].[conversations]
    ADD CONSTRAINT [ck_conversations_65] CHECK ([version]>(0));
GO
ALTER TABLE [support].[conversations]
    ADD CONSTRAINT [ck_conversations_66] CHECK ([conversationStatus]='Open' AND [closedAt] IS NULL AND [closedById] IS NULL OR [conversationStatus]='Closed' AND [closedAt] IS NOT NULL);
GO
ALTER TABLE [support].[conversations]
    ADD CONSTRAINT [ck_conversations_67] CHECK ([relatedDisputeId] IS NULL OR [disputeId] IS NULL AND [transactionId] IS NULL);
GO
ALTER TABLE [support].[conversations]
    ADD CONSTRAINT [fk_conversations_assignedToId]
    FOREIGN KEY ([assignedToId])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [support].[conversations]
    ADD CONSTRAINT [fk_conversations_closedById]
    FOREIGN KEY ([closedById])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [support].[conversations]
    ADD CONSTRAINT [fk_conversations_concernId]
    FOREIGN KEY ([concernId])
    REFERENCES [support].[concerns] ([id]);
GO
ALTER TABLE [support].[conversations]
    ADD CONSTRAINT [fk_conversations_customerId]
    FOREIGN KEY ([customerId])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [support].[conversations]
    ADD CONSTRAINT [fk_conversations_disputeId]
    FOREIGN KEY ([disputeId])
    REFERENCES [disputes].[disputes] ([id]);
GO
ALTER TABLE [support].[conversations]
    ADD CONSTRAINT [fk_conversations_relatedDisputeId]
    FOREIGN KEY ([relatedDisputeId])
    REFERENCES [disputes].[disputes] ([id]);
GO
ALTER TABLE [support].[conversations]
    ADD CONSTRAINT [fk_conversations_transactionId]
    FOREIGN KEY ([transactionId])
    REFERENCES [transactions].[transactions] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_conversations_125]
    ON [support].[conversations] ([assignedToId] ASC, [conversationStatus] ASC, [createdAt] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_conversations_126]
    ON [support].[conversations] ([customerId] ASC, [relatedDisputeId] ASC, [conversationStatus] ASC, [createdAt] ASC)
    WHERE ([relatedDisputeId] IS NOT NULL);
GO
CREATE NONCLUSTERED INDEX [ix_conversations_127]
    ON [support].[conversations] ([conversationStatus] ASC, [queueType] ASC, [priority] ASC, [createdAt] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_conversations_128]
    ON [support].[conversations] ([customerId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_conversations_129]
    ON [support].[conversations] ([disputeId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_conversations_130]
    ON [support].[conversations] ([transactionId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_conversations_131]
    ON [support].[conversations] ([transactionId] ASC, [customerId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_conversations_132]
    ON [support].[conversations] ([disputeId] ASC)
    WHERE ([disputeId] IS NOT NULL AND [conversationStatus]='Open');
GO
ALTER TABLE [support].[messages]
    ADD CONSTRAINT [ck_messages_60] CHECK ([enquiryDetails] IS NULL OR isjson([enquiryDetails])=(1));
GO
ALTER TABLE [support].[messages]
    ADD CONSTRAINT [fk_messages_authorId]
    FOREIGN KEY ([authorId])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [support].[messages]
    ADD CONSTRAINT [fk_messages_threadId]
    FOREIGN KEY ([threadId])
    REFERENCES [support].[conversations] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_messages_120]
    ON [support].[messages] ([threadId] ASC, [sequenceNo] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_messages_121]
    ON [support].[messages] ([threadId] ASC, [createdAt] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_messages_122]
    ON [support].[messages] ([authorId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_messages_123]
    ON [support].[messages] ([threadId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_messages_124]
    ON [support].[messages] ([authorId] ASC, [clientMessageId] ASC)
    WHERE ([clientMessageId] IS NOT NULL);
GO
ALTER TABLE [support].[readStates]
    ADD CONSTRAINT [ckSupportReadSequence] CHECK ([lastReadSequence]>=(0));
GO
ALTER TABLE [support].[readStates]
    ADD CONSTRAINT [fkSupportReadConversation]
    FOREIGN KEY ([conversationId])
    REFERENCES [support].[conversations] ([id]);
GO
ALTER TABLE [support].[readStates]
    ADD CONSTRAINT [fkSupportReadUser]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
ALTER TABLE [support].[staffCapacity]
    ADD CONSTRAINT [ck_staffCapacity_55] CHECK ([availability]='Offline' OR [availability]='Busy' OR [availability]='Available');
GO
ALTER TABLE [support].[staffCapacity]
    ADD CONSTRAINT [ck_staffCapacity_56] CHECK ([capacity]>=(1) AND [capacity]<=(50));
GO
ALTER TABLE [support].[staffCapacity]
    ADD CONSTRAINT [fk_staffCapacity_userId]
    FOREIGN KEY ([userId])
    REFERENCES [identity].[users] ([id]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_staffCapacity_117]
    ON [support].[staffCapacity] ([userId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_staffCapacity_118]
    ON [support].[staffCapacity] ([userId] ASC);
GO
ALTER TABLE [transactions].[bankLinks]
    ADD CONSTRAINT [fk_bankLinks_accountId]
    FOREIGN KEY ([accountId])
    REFERENCES [accounts].[accounts] ([id]);
GO
ALTER TABLE [transactions].[bankLinks]
    ADD CONSTRAINT [fk_bankLinks_transactionId]
    FOREIGN KEY ([transactionId])
    REFERENCES [transactions].[transactions] ([id]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_bankLinks_16]
    ON [transactions].[bankLinks] ([accountId] ASC, [externalTransactionHash] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_bankLinks_17]
    ON [transactions].[bankLinks] ([accountId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_bankLinks_18]
    ON [transactions].[bankLinks] ([transactionId] ASC);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_bankLinks_19]
    ON [transactions].[bankLinks] ([transactionId] ASC);
GO
ALTER TABLE [transactions].[duplicateConfirmations]
    ADD CONSTRAINT [ck_duplicateConfirmations_75] CHECK ([confirmationSource]='LocalBankFeed');
GO
ALTER TABLE [transactions].[duplicateConfirmations]
    ADD CONSTRAINT [ck_duplicateConfirmations_76] CHECK ([transactionId]<>[originalTransactionId]);
GO
ALTER TABLE [transactions].[duplicateConfirmations]
    ADD CONSTRAINT [fk_duplicateConfirmations_originalTransactionId]
    FOREIGN KEY ([originalTransactionId])
    REFERENCES [transactions].[transactions] ([id]);
GO
ALTER TABLE [transactions].[duplicateConfirmations]
    ADD CONSTRAINT [fk_duplicateConfirmations_transactionId]
    FOREIGN KEY ([transactionId])
    REFERENCES [transactions].[transactions] ([id]);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_duplicateConfirmations_144]
    ON [transactions].[duplicateConfirmations] ([transactionId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_duplicateConfirmations_145]
    ON [transactions].[duplicateConfirmations] ([originalTransactionId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_duplicateConfirmations_146]
    ON [transactions].[duplicateConfirmations] ([transactionId] ASC);
GO
ALTER TABLE [transactions].[transactions]
    ADD CONSTRAINT [ck_transactions_77] CHECK ([amount]>=(0));
GO
ALTER TABLE [transactions].[transactions]
    ADD CONSTRAINT [ck_transactions_78] CHECK (len([currencyCode])=(3) AND NOT [currencyCode] like '%[^A-Z]%');
GO
ALTER TABLE [transactions].[transactions]
    ADD CONSTRAINT [ck_transactions_79] CHECK ([direction]='Credit' OR [direction]='Debit');
GO
ALTER TABLE [transactions].[transactions]
    ADD CONSTRAINT [ck_transactions_80] CHECK ([transactionStatus]='Reversed' OR [transactionStatus]='Pending' OR [transactionStatus]='Posted');
GO
ALTER TABLE [transactions].[transactions]
    ADD CONSTRAINT [fk_transactions_accountId]
    FOREIGN KEY ([accountId])
    REFERENCES [accounts].[accounts] ([id]);
GO
ALTER TABLE [transactions].[transactions]
    ADD CONSTRAINT [fk_transactions_transactionTypeId]
    FOREIGN KEY ([transactionTypeId])
    REFERENCES [configuration].[transactionTypes] ([id]);
GO
CREATE NONCLUSTERED INDEX [ix_transactions_148]
    ON [transactions].[transactions] ([accountId] ASC, [transactionAt] DESC);
GO
CREATE NONCLUSTERED INDEX [ix_transactions_149]
    ON [transactions].[transactions] ([accountId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_transactions_150]
    ON [transactions].[transactions] ([publicId] ASC, [accountId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_transactions_151]
    ON [transactions].[transactions] ([transactionTypeId] ASC);
GO
CREATE NONCLUSTERED INDEX [ix_transactions_152]
    ON [transactions].[transactions] ([accountId] ASC, [createdAt] DESC)
    WHERE ([archivedAt] IS NULL);
GO
CREATE UNIQUE NONCLUSTERED INDEX [ix_transactions_153]
    ON [transactions].[transactions] ([accountId] ASC, [transactionReference] ASC);
GO

CREATE TRIGGER [accounts].[tr_account_identity]
    ON [accounts].[accounts]
    AFTER UPDATE
    AS
BEGIN
           IF EXISTS (SELECT 1
                      FROM   inserted AS i
                             INNER JOIN
                             deleted AS d
                             ON d.[id] = i.[id]
                      WHERE  (i.[currencyCode] <> d.[currencyCode]
                              OR i.[userId] <> d.[userId])
                             AND EXISTS (SELECT 1
                                         FROM   [ledger].[projectionEntries] AS l
                                         WHERE  l.[accountId] = d.[id]))
               THROW 51000, 'A journalled account cannot change currency or owner.', 1;

END

GO

CREATE TRIGGER [identity].[tr_accountDeletionChallenges_publicIdentity]
ON [identity].[accountDeletionChallenges]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [identity].[tr_accountDeletionRequests_publicIdentity]
ON [identity].[accountDeletionRequests]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [identity].[tr_accountDeletionRequests_retainRecord]
ON [identity].[accountDeletionRequests] AFTER DELETE AS
BEGIN
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51000,'Retained business records cannot be physically deleted; use the authorized lifecycle operation.',1;

END;

GO

CREATE TRIGGER [accounts].[tr_accounts_no_hard_delete]
    ON [accounts].[accounts]
    INSTEAD OF DELETE
    AS
BEGIN
           SET NOCOUNT ON;

THROW 51000, 'Use the account closure workflow. Account records cannot be physically deleted.', 1;

END

GO

CREATE TRIGGER [accounts].[tr_accounts_publicIdentity]
ON [accounts].[accounts]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [accounts].[tr_accounts_trackedRemoval]
ON [accounts].[accounts] AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;

IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id=i.id
        WHERE d.[deletedAt] IS NOT NULL AND (i.[deletedAt] IS NULL OR i.[deletedById] IS NULL OR i.[deletedAt]<>d.[deletedAt] OR i.[deletedById]<>d.[deletedById])
    ) THROW 51000,'Removal metadata and retained records cannot be rewritten.',1;

INSERT INTO audit.[auditEvents]
        ([publicId],[actorUserId],action,[entityType],[entityPublicId],[occurredAt],[correlationId],[safeMetadata],outcome)
    SELECT NEWID(),i.[deletedById],'RecordSoftDeleted','account',i.[publicId],i.[deletedAt],CONVERT(nvarchar(36),NEWID()),
        (SELECT CAST(1 AS bit) AS retained FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),'Success'
    FROM inserted i LEFT JOIN deleted previous ON previous.id=i.id
    WHERE i.[deletedAt] IS NOT NULL AND previous.[deletedAt] IS NULL;

END;

GO

CREATE TRIGGER [audit].[tr_actorSnapshots_immutable]
ON [audit].[actorSnapshots]
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

THROW 51000, 'Recorded actor identities are immutable.', 1;

END;

GO

CREATE TRIGGER [monitoring].[tr_apiMetricSummaries_key]
ON [monitoring].[apiMetricSummaries] AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([bucketStart]) OR UPDATE([httpMethod]) OR UPDATE([route]) OR UPDATE([statusCode])
        THROW 51000, 'Metric aggregate keys are immutable.', 1;

END;

GO

CREATE TRIGGER [audit].[tr_audit_immutable]
    ON [audit].[auditEvents]
    AFTER UPDATE, DELETE
    AS THROW 51000, 'Append-only audit record.', 1;

GO

CREATE TRIGGER [audit].[tr_auditEvents_publicIdentity]
ON [audit].[auditEvents]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [disputes].[tr_auto_decision_immutable]
    ON [disputes].[automaticDecisions]
    INSTEAD OF UPDATE, DELETE
    AS THROW 51000, 'Append-only automatic decision.', 1;

GO

CREATE TRIGGER [disputes].[tr_automaticDecisions_publicIdentity]
ON [disputes].[automaticDecisions]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [integration].[tr_bankingRegenerations_publicIdentity]
ON [integration].[bankingRegenerations]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [transactions].[tr_bankLinks_retainRecord]
ON [transactions].[bankLinks] AFTER DELETE AS
BEGIN
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51000,'Retained business records cannot be physically deleted; use the authorized lifecycle operation.',1;

END;

GO

CREATE TRIGGER [accounts].[tr_bankLinks_retainRecord]
ON [accounts].[bankLinks] AFTER DELETE AS
BEGIN
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51000,'Retained business records cannot be physically deleted; use the authorized lifecycle operation.',1;

END;

GO
CREATE TRIGGER transactions.tr_bankTransaction_originalValues
ON transactions.transactions
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT previous.id
        FROM deleted previous
        JOIN inserted currentRecord ON currentRecord.id=previous.id
        WHERE (
            EXISTS (SELECT 1 FROM transactions.bankLinks link WHERE link.transactionId=previous.id)
            OR EXISTS (SELECT 1 FROM integration.accountMigrations adoption
                WHERE adoption.accountId=previous.accountId AND adoption.state='Active')
            OR EXISTS (SELECT 1 FROM disputes.disputes case_record WHERE case_record.transactionId=previous.id)
        ) AND EXISTS (
            SELECT
                previous.[accountId],
                DATALENGTH(previous.[transactionReference]),
                CONVERT(varbinary(max),previous.[transactionReference]),
                CONVERT(varbinary(max),previous.[transactionAt]),
                DATALENGTH(previous.[description]),
                CONVERT(varbinary(max),previous.[description]),
                DATALENGTH(previous.[merchantName]),
                CONVERT(varbinary(max),previous.[merchantName]),
                previous.[amount],
                DATALENGTH(previous.[currencyCode]),
                CONVERT(varbinary(max),previous.[currencyCode]),
                previous.[transactionTypeId],
                DATALENGTH(previous.[direction]),
                CONVERT(varbinary(max),previous.[direction]),
                CONVERT(varbinary(max),previous.[createdAt])
            EXCEPT
            SELECT
                currentRecord.[accountId],
                DATALENGTH(currentRecord.[transactionReference]),
                CONVERT(varbinary(max),currentRecord.[transactionReference]),
                CONVERT(varbinary(max),currentRecord.[transactionAt]),
                DATALENGTH(currentRecord.[description]),
                CONVERT(varbinary(max),currentRecord.[description]),
                DATALENGTH(currentRecord.[merchantName]),
                CONVERT(varbinary(max),currentRecord.[merchantName]),
                currentRecord.[amount],
                DATALENGTH(currentRecord.[currencyCode]),
                CONVERT(varbinary(max),currentRecord.[currencyCode]),
                currentRecord.[transactionTypeId],
                DATALENGTH(currentRecord.[direction]),
                CONVERT(varbinary(max),currentRecord.[direction]),
                CONVERT(varbinary(max),currentRecord.[createdAt])
        )
    )
        THROW 51058,'Original bank transaction values are immutable; reconcile through a separate financial record.',1;
END;

GO

CREATE TRIGGER [transactions].[tr_canonical_transaction_projection]
    ON [transactions].[transactions]
    AFTER INSERT, UPDATE, DELETE
    AS
BEGIN
           SET NOCOUNT ON;

IF ISNULL(TRY_CONVERT (INT, SESSION_CONTEXT(N'canonical_projection')), 0) = 1
               RETURN;

IF EXISTS (SELECT 1
                      FROM   inserted AS i
                             LEFT OUTER JOIN
                             deleted AS d
                             ON d.[id] = i.[id]
                             INNER JOIN
                             [integration].[accountMigrations] AS m
                             ON m.[accountId] = i.[accountId]
                                AND m.[state] = 'Active'
                      WHERE  d.[id] IS NULL
                             OR i.[amount] <> d.[amount]
                             OR i.[currencyCode] <> d.[currencyCode]
                             OR i.[direction] <> d.[direction]
                             OR i.[transactionStatus] <> d.[transactionStatus]
                             OR i.[accountId] <> d.[accountId])
               THROW 51000, 'Canonical financial records can only be projected from their ledger workflow.', 1;

END

GO

CREATE TRIGGER [audit].[tr_captureAuditActor]
ON [audit].[auditEvents]
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

INSERT INTO [audit].[actorSnapshots] (
        [sourceKind], [auditEventId], [actorUserId],
        [firstName], [middleName], [lastName], [accountName],
        [loginIdentifier], [roleKeys], [actorKind], [provenance]
    )
    SELECT
        N'Audit', i.[id], i.[actorUserId],
        CASE WHEN u.[deletedAt] IS NULL THEN p.[firstName] END,
        CASE WHEN u.[deletedAt] IS NULL THEN p.[middleName] END,
        CASE WHEN u.[deletedAt] IS NULL THEN p.[lastName] END,
        CASE WHEN i.[actorUserId] IS NULL THEN N'System'
             WHEN u.[deletedAt] IS NOT NULL THEN N'Closed account'
             ELSE NULLIF(p.[displayName], N'') END,
        CASE WHEN u.[deletedAt] IS NULL THEN u.[loginIdentifier] END,
        (SELECT STRING_AGG(r.[code], N'|')
         FROM [identity].[userRoles] ur
         JOIN [identity].[roles] r ON r.[id]=ur.[roleId]
         WHERE ur.[userId]=u.[id]),
        CASE WHEN i.[actorUserId] IS NULL THEN N'System' ELSE N'User' END,
        CASE WHEN u.[deletedAt] IS NOT NULL THEN N'ErasedProfile' ELSE N'Recorded' END
    FROM inserted i
    LEFT JOIN [identity].[users] u ON u.[id]=i.[actorUserId]
    LEFT JOIN [identity].[customerProfiles] p ON p.[userId]=u.[id];

END;

GO

CREATE TRIGGER [disputes].[tr_captureAutomaticActor]
ON [disputes].[automaticDecisions]
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

INSERT INTO [audit].[actorSnapshots] (
        [sourceKind], [automaticDecisionId], [actorUserId],
        [firstName], [middleName], [lastName], [accountName],
        [loginIdentifier], [roleKeys], [actorKind], [provenance]
    )
    SELECT
        N'Automatic', i.[id], CAST(NULL AS bigint),
        CASE WHEN u.[deletedAt] IS NULL THEN p.[firstName] END,
        CASE WHEN u.[deletedAt] IS NULL THEN p.[middleName] END,
        CASE WHEN u.[deletedAt] IS NULL THEN p.[lastName] END,
        CASE WHEN CAST(NULL AS bigint) IS NULL THEN N'System'
             WHEN u.[deletedAt] IS NOT NULL THEN N'Closed account'
             ELSE NULLIF(p.[displayName], N'') END,
        CASE WHEN u.[deletedAt] IS NULL THEN u.[loginIdentifier] END,
        (SELECT STRING_AGG(r.[code], N'|')
         FROM [identity].[userRoles] ur
         JOIN [identity].[roles] r ON r.[id]=ur.[roleId]
         WHERE ur.[userId]=u.[id]),
        CASE WHEN CAST(NULL AS bigint) IS NULL THEN N'System' ELSE N'User' END,
        CASE WHEN u.[deletedAt] IS NOT NULL THEN N'ErasedProfile' ELSE N'Recorded' END
    FROM inserted i
    LEFT JOIN [identity].[users] u ON u.[id]=CAST(NULL AS bigint)
    LEFT JOIN [identity].[customerProfiles] p ON p.[userId]=u.[id];

END;

GO

CREATE TRIGGER [disputes].[tr_captureEvidenceActor]
ON [disputes].[evidence]
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

INSERT INTO [audit].[actorSnapshots] (
        [sourceKind], [evidenceId], [actorUserId],
        [firstName], [middleName], [lastName], [accountName],
        [loginIdentifier], [roleKeys], [actorKind], [provenance]
    )
    SELECT
        N'Evidence', i.[id], i.[uploadedById],
        CASE WHEN u.[deletedAt] IS NULL THEN p.[firstName] END,
        CASE WHEN u.[deletedAt] IS NULL THEN p.[middleName] END,
        CASE WHEN u.[deletedAt] IS NULL THEN p.[lastName] END,
        CASE WHEN i.[uploadedById] IS NULL THEN N'System'
             WHEN u.[deletedAt] IS NOT NULL THEN N'Closed account'
             ELSE NULLIF(p.[displayName], N'') END,
        CASE WHEN u.[deletedAt] IS NULL THEN u.[loginIdentifier] END,
        (SELECT STRING_AGG(r.[code], N'|')
         FROM [identity].[userRoles] ur
         JOIN [identity].[roles] r ON r.[id]=ur.[roleId]
         WHERE ur.[userId]=u.[id]),
        CASE WHEN i.[uploadedById] IS NULL THEN N'System' ELSE N'User' END,
        CASE WHEN u.[deletedAt] IS NOT NULL THEN N'ErasedProfile' ELSE N'Recorded' END
    FROM inserted i
    LEFT JOIN [identity].[users] u ON u.[id]=i.[uploadedById]
    LEFT JOIN [identity].[customerProfiles] p ON p.[userId]=u.[id];

END;

GO

CREATE TRIGGER [disputes].[tr_captureHistoryActor]
ON [disputes].[statusHistory]
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

INSERT INTO [audit].[actorSnapshots] (
        [sourceKind], [historyId], [actorUserId],
        [firstName], [middleName], [lastName], [accountName],
        [loginIdentifier], [roleKeys], [actorKind], [provenance]
    )
    SELECT
        N'History', i.[id], i.[actorUserId],
        CASE WHEN u.[deletedAt] IS NULL THEN p.[firstName] END,
        CASE WHEN u.[deletedAt] IS NULL THEN p.[middleName] END,
        CASE WHEN u.[deletedAt] IS NULL THEN p.[lastName] END,
        CASE WHEN i.[actorUserId] IS NULL THEN N'System'
             WHEN u.[deletedAt] IS NOT NULL THEN N'Closed account'
             ELSE NULLIF(p.[displayName], N'') END,
        CASE WHEN u.[deletedAt] IS NULL THEN u.[loginIdentifier] END,
        (SELECT STRING_AGG(r.[code], N'|')
         FROM [identity].[userRoles] ur
         JOIN [identity].[roles] r ON r.[id]=ur.[roleId]
         WHERE ur.[userId]=u.[id]),
        CASE WHEN i.[actorUserId] IS NULL THEN N'System' ELSE N'User' END,
        CASE WHEN u.[deletedAt] IS NOT NULL THEN N'ErasedProfile' ELSE N'Recorded' END
    FROM inserted i
    LEFT JOIN [identity].[users] u ON u.[id]=i.[actorUserId]
    LEFT JOIN [identity].[customerProfiles] p ON p.[userId]=u.[id];

END;

GO

CREATE TRIGGER [support].[tr_captureMessageActor]
ON [support].[messages]
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

INSERT INTO [audit].[actorSnapshots] (
        [sourceKind], [messageId], [actorUserId],
        [firstName], [middleName], [lastName], [accountName],
        [loginIdentifier], [roleKeys], [actorKind], [provenance]
    )
    SELECT
        N'Message', i.[id], i.[authorId],
        CASE WHEN u.[deletedAt] IS NULL THEN p.[firstName] END,
        CASE WHEN u.[deletedAt] IS NULL THEN p.[middleName] END,
        CASE WHEN u.[deletedAt] IS NULL THEN p.[lastName] END,
        CASE WHEN i.[authorId] IS NULL THEN N'System'
             WHEN u.[deletedAt] IS NOT NULL THEN N'Closed account'
             ELSE NULLIF(p.[displayName], N'') END,
        CASE WHEN u.[deletedAt] IS NULL THEN u.[loginIdentifier] END,
        (SELECT STRING_AGG(r.[code], N'|')
         FROM [identity].[userRoles] ur
         JOIN [identity].[roles] r ON r.[id]=ur.[roleId]
         WHERE ur.[userId]=u.[id]),
        CASE WHEN i.[authorId] IS NULL THEN N'System' ELSE N'User' END,
        CASE WHEN u.[deletedAt] IS NOT NULL THEN N'ErasedProfile' ELSE N'Recorded' END
    FROM inserted i
    LEFT JOIN [identity].[users] u ON u.[id]=i.[authorId]
    LEFT JOIN [identity].[customerProfiles] p ON p.[userId]=u.[id];

END;

GO

CREATE TRIGGER [disputes].[tr_captureNoteActor]
ON [disputes].[notes]
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

INSERT INTO [audit].[actorSnapshots] (
        [sourceKind], [noteId], [actorUserId],
        [firstName], [middleName], [lastName], [accountName],
        [loginIdentifier], [roleKeys], [actorKind], [provenance]
    )
    SELECT
        N'Note', i.[id], i.[authorUserId],
        CASE WHEN u.[deletedAt] IS NULL THEN p.[firstName] END,
        CASE WHEN u.[deletedAt] IS NULL THEN p.[middleName] END,
        CASE WHEN u.[deletedAt] IS NULL THEN p.[lastName] END,
        CASE WHEN i.[authorUserId] IS NULL THEN N'System'
             WHEN u.[deletedAt] IS NOT NULL THEN N'Closed account'
             ELSE NULLIF(p.[displayName], N'') END,
        CASE WHEN u.[deletedAt] IS NULL THEN u.[loginIdentifier] END,
        (SELECT STRING_AGG(r.[code], N'|')
         FROM [identity].[userRoles] ur
         JOIN [identity].[roles] r ON r.[id]=ur.[roleId]
         WHERE ur.[userId]=u.[id]),
        CASE WHEN i.[authorUserId] IS NULL THEN N'System' ELSE N'User' END,
        CASE WHEN u.[deletedAt] IS NOT NULL THEN N'ErasedProfile' ELSE N'Recorded' END
    FROM inserted i
    LEFT JOIN [identity].[users] u ON u.[id]=i.[authorUserId]
    LEFT JOIN [identity].[customerProfiles] p ON p.[userId]=u.[id];

END;

GO

CREATE TRIGGER [identity].[tr_captureSecurityActor]
ON [identity].[securityEvents]
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

INSERT INTO [audit].[actorSnapshots] (
        [sourceKind], [securityEventId], [actorUserId],
        [firstName], [middleName], [lastName], [accountName],
        [loginIdentifier], [roleKeys], [actorKind], [provenance]
    )
    SELECT
        N'Security', i.[id], i.[userId],
        CASE WHEN u.[deletedAt] IS NULL THEN p.[firstName] END,
        CASE WHEN u.[deletedAt] IS NULL THEN p.[middleName] END,
        CASE WHEN u.[deletedAt] IS NULL THEN p.[lastName] END,
        CASE WHEN i.[userId] IS NULL THEN N'Unauthenticated request'
             WHEN u.[deletedAt] IS NOT NULL THEN N'Closed account'
             ELSE NULLIF(p.[displayName], N'') END,
        CASE WHEN u.[deletedAt] IS NULL THEN u.[loginIdentifier] END,
        (SELECT STRING_AGG(r.[code], N'|')
         FROM [identity].[userRoles] ur
         JOIN [identity].[roles] r ON r.[id]=ur.[roleId]
         WHERE ur.[userId]=u.[id]),
        CASE WHEN i.[userId] IS NULL THEN N'Anonymous' ELSE N'User' END,
        CASE WHEN u.[deletedAt] IS NOT NULL THEN N'ErasedProfile' ELSE N'Recorded' END
    FROM inserted i
    LEFT JOIN [identity].[users] u ON u.[id]=i.[userId]
    LEFT JOIN [identity].[customerProfiles] p ON p.[userId]=u.[id];

END;

GO

CREATE TRIGGER disputes.tr_caseStatuses_lifecycle ON disputes."caseStatuses" AFTER UPDATE,DELETE AS
BEGIN
    IF TRIGGER_NESTLEVEL(OBJECT_ID('disputes.tr_caseStatuses_lifecycle'))>1 RETURN;

IF NOT EXISTS(SELECT 1 FROM inserted) OR UPDATE(code) OR UPDATE(revision) OR UPDATE("updatedAtUtc")
        THROW 51065,'Workflow codes are retained; only display metadata can change.',1;

UPDATE current_status SET revision=previous_status.revision+1,"updatedAtUtc"=SYSUTCDATETIME()
    FROM disputes."caseStatuses" current_status JOIN deleted previous_status ON previous_status.code=current_status.code;

END;

GO

CREATE TRIGGER [support].[tr_concerns_publicIdentity]
ON [support].[concerns]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [identity].[tr_contact_login_routing_changed]
    ON [identity].[customerProfiles]
    AFTER UPDATE
    AS
BEGIN
           SET NOCOUNT ON;

UPDATE [identity].[userSessions]
           SET    [revokedAt] = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')
           WHERE  [revokedAt] IS NULL
                  AND [userId] IN (SELECT i.[userId]
                                   FROM   inserted AS i
                                          INNER JOIN
                                          deleted AS d
                                          ON d.[userId] = i.[userId]
                                   WHERE  i.[contactLoginEnabled] <> d.[contactLoginEnabled]);

UPDATE [identity].[trustedLoginDevices]
           SET    [revokedAt] = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')
           WHERE  [revokedAt] IS NULL
                  AND [userId] IN (SELECT i.[userId]
                                   FROM   inserted AS i
                                          INNER JOIN
                                          deleted AS d
                                          ON d.[userId] = i.[userId]
                                   WHERE  i.[contactLoginEnabled] <> d.[contactLoginEnabled]);

UPDATE [identity].[identityChallenges]
           SET    [state]            = 'Failed',
                  [protectedPayload] = ''
           WHERE  [state] IN ('Ready', 'Pending')
                  AND [userId] IN (SELECT i.[userId]
                                   FROM   inserted AS i
                                          INNER JOIN
                                          deleted AS d
                                          ON d.[userId] = i.[userId]
                                   WHERE  i.[contactLoginEnabled] <> d.[contactLoginEnabled]);

END

GO

CREATE TRIGGER [identity].[tr_contactVerificationChallenges_publicIdentity]
ON [identity].[contactVerificationChallenges]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [support].[tr_conversations_publicIdentity]
ON [support].[conversations]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [support].[tr_conversations_retainRecord]
ON [support].[conversations] AFTER DELETE AS
BEGIN
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51000,'Retained business records cannot be physically deleted; use the authorized lifecycle operation.',1;

END;

GO

CREATE TRIGGER [identity].[tr_customerProfiles_retainRecord]
ON [identity].[customerProfiles] AFTER DELETE AS
BEGIN
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51000,'Retained business records cannot be physically deleted; use the authorized lifecycle operation.',1;

END;

GO

CREATE TRIGGER [notifications].[tr_deliveries_publicIdentity]
ON [notifications].[deliveries]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [disputes].[tr_dispute_finance_pending]
    ON [disputes].[disputes]
    AFTER UPDATE
    AS
BEGIN
           SET NOCOUNT ON;

IF ISNULL(TRY_CONVERT (INT, SESSION_CONTEXT(N'canonical_projection')), 0) = 1
               RETURN;

IF EXISTS (SELECT 1
                      FROM   inserted AS i
                             INNER JOIN
                             deleted AS d
                             ON d.[id] = i.[id]
                             INNER JOIN
                             [disputes].[financialCommands] AS p
                             ON p.[disputeId] = i.[id]
                                AND p.[state] = 'Queued'
                      WHERE  i.[status] <> d.[status])
               THROW 51000, 'A financial request is processing. Wait for its confirmed outcome before changing the case status.', 1;

END

GO

CREATE TRIGGER [configuration].[tr_disputeCategories_codeIdentity]
ON [configuration].[disputeCategories] AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([code]) AND EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
        WHERE CONVERT(varbinary(max), i.code) <> CONVERT(varbinary(max), d.code)
    ) THROW 51000, 'Reference codes are immutable; create a new reference entry.', 1;

END;

GO

CREATE TRIGGER [configuration].[tr_disputeCategories_publicIdentity]
ON [configuration].[disputeCategories]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [configuration].[tr_disputeCategories_retainRecord]
ON [configuration].[disputeCategories] AFTER DELETE AS
BEGIN
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51000,'Retained business records cannot be physically deleted; use the authorized lifecycle operation.',1;

END;

GO

CREATE TRIGGER configuration.tr_disputeInputPolicies_identity ON configuration.disputeInputPolicies AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(id) THROW 51000, 'Policy identifiers cannot be changed.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.id=i.id WHERE i.publicId<>d.publicId)
        THROW 51000, 'Policy identifiers cannot be changed.', 1;
END;

GO

CREATE TRIGGER [configuration].[tr_disputeInputPolicies_retainRecord]
ON [configuration].[disputeInputPolicies] AFTER DELETE AS
BEGIN
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51000,'Retained business records cannot be physically deleted; use the authorized lifecycle operation.',1;

END;

GO

CREATE TRIGGER [disputes].[tr_disputes_publicIdentity]
ON [disputes].[disputes]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [disputes].[tr_disputes_retainRecord]
ON [disputes].[disputes] AFTER DELETE AS
BEGIN
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51000,'Retained business records cannot be physically deleted; use the authorized lifecycle operation.',1;

END;

GO

CREATE TRIGGER [identity].[tr_disputeVerifications_publicIdentity]
ON [identity].[disputeVerifications]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [transactions].[tr_duplicate_proof_immutable]
    ON [transactions].[duplicateConfirmations]
    INSTEAD OF UPDATE, DELETE
    AS THROW 51000, 'Append-only duplicate proof.', 1;

GO

CREATE TRIGGER [notifications].[tr_events_publicIdentity]
ON [notifications].[events]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [disputes].[tr_evidence_payloads_immutable]
    ON [disputes].[evidencePayloads]
    AFTER UPDATE, DELETE
    AS
BEGIN
           THROW 51000, 'Evidence payloads are immutable.', 1;

END

GO

CREATE TRIGGER [disputes].[tr_evidence_publicIdentity]
ON [disputes].[evidence]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [disputes].[tr_evidence_retainRecord]
ON [disputes].[evidence] AFTER DELETE AS
BEGIN
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51000,'Retained business records cannot be physically deleted; use the authorized lifecycle operation.',1;

END;

GO
CREATE TRIGGER [disputes].[tr_evidence_trackedRemoval]
ON [disputes].[evidence] AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id=i.id
        WHERE d.[deletedAt] IS NOT NULL
    ) THROW 51000,'Removal metadata and retained records cannot be rewritten.',1;

    INSERT INTO audit.[auditEvents]
        ([publicId],[actorUserId],action,[entityType],[entityPublicId],[occurredAt],[correlationId],[safeMetadata],outcome)
    SELECT NEWID(),i.[deletedById],'RecordSoftDeleted','evidence',i.[publicId],i.[deletedAt],CONVERT(nvarchar(36),NEWID()),
        (SELECT CAST(1 AS bit) AS retained, i.[sha256] AS sha256, i.[originalSha256] AS originalSha256, (SELECT d.[publicId] FROM disputes.disputes d WHERE d.id=i.[disputeId]) AS disputeId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),'Success'
    FROM inserted i LEFT JOIN deleted previous ON previous.id=i.id
    WHERE i.[deletedAt] IS NOT NULL AND previous.[deletedAt] IS NULL;
END;

GO

CREATE TRIGGER [disputes].[tr_evidenceAccessGrants_publicIdentity]
ON [disputes].[evidenceAccessGrants]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [disputes].[tr_evidenceAccessGrants_retainRecord]
ON [disputes].[evidenceAccessGrants] AFTER DELETE AS
BEGIN
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51000,'Retained business records cannot be physically deleted; use the authorized lifecycle operation.',1;

END;

GO

CREATE TRIGGER [disputes].[tr_evidencePayloads_publicIdentity]
ON [disputes].[evidencePayloads]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [disputes].[tr_financialCommands_publicIdentity]
ON [disputes].[financialCommands]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [identity].[tr_identityChallenges_publicIdentity]
ON [identity].[identityChallenges]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [ledger].[tr_ledger_immutable]
    ON [ledger].[projectionEntries]
    INSTEAD OF UPDATE, DELETE
    AS THROW 51000, 'Append-only financial record.', 1;

GO

CREATE TRIGGER [integration].[tr_local_regeneration_immutable]
    ON [integration].[bankingRegenerations]
    INSTEAD OF UPDATE, DELETE
    AS
BEGIN
           SET NOCOUNT ON;

THROW 51000, 'Append-only banking regeneration audit.', 1;

END

GO

CREATE TRIGGER [integration].[tr_localTransactionBatches_publicIdentity]
ON [integration].[localTransactionBatches]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [support].[tr_messages_publicIdentity]
ON [support].[messages]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [support].[tr_messages_retainContent]
ON [support].[messages] AFTER UPDATE AS
BEGIN
    IF EXISTS(SELECT 1 FROM inserted)
        THROW 51000,'Recorded case history and messages are append-only.',1;

END;

GO

CREATE TRIGGER [support].[tr_messages_retainRecord]
ON [support].[messages] AFTER DELETE AS
BEGIN
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51000,'Retained business records cannot be physically deleted; use the authorized lifecycle operation.',1;

END;

GO

CREATE TRIGGER [monitoring].[tr_metricBatches_publicIdentity]
ON [monitoring].[metricBatches] AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id WHERE i.publicId <> d.publicId
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [disputes].[tr_notes_publicIdentity]
ON [disputes].[notes]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [disputes].[tr_notes_retainContent]
ON [disputes].[notes] AFTER UPDATE AS
BEGIN
    IF EXISTS(SELECT 1 FROM inserted)
        THROW 51000,'Recorded case history and messages are append-only.',1;

END;

GO

CREATE TRIGGER [disputes].[tr_notes_retainRecord]
ON [disputes].[notes] AFTER DELETE AS
BEGIN
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51000,'Retained business records cannot be physically deleted; use the authorized lifecycle operation.',1;

END;

GO

CREATE TRIGGER [notifications].[tr_notifications_publicIdentity]
ON [notifications].[notifications]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [identity].[tr_permissions_codeIdentity]
ON [identity].[permissions] AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([code]) AND EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
        WHERE CONVERT(varbinary(max), i.code) <> CONVERT(varbinary(max), d.code)
    ) THROW 51000, 'Reference codes are immutable; create a new reference entry.', 1;

END;

GO

CREATE TRIGGER [notifications].[tr_pushSubscriptions_publicIdentity]
ON [notifications].[pushSubscriptions]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [transactions].[tr_recall_transaction_immutable]
    ON [transactions].[transactions]
    AFTER UPDATE
    AS
BEGIN
           IF EXISTS (SELECT 1
                      FROM   inserted AS i
                             INNER JOIN
                             deleted AS d
                             ON d.[id] = i.[id]
                      WHERE  EXISTS (SELECT 1
                                     FROM   [disputes].[refundRecalls] AS r
                                     WHERE  r.[reversalTransactionId] = d.[id])
                             AND (i.[accountId] <> d.[accountId]
                                  OR i.[amount] <> d.[amount]
                                  OR i.[currencyCode] <> d.[currencyCode]
                                  OR i.[direction] <> d.[direction]
                                  OR i.[transactionStatus] <> d.[transactionStatus]
                                  OR i.[transactionTypeId] <> d.[transactionTypeId]
                                  OR i.[transactionAt] <> d.[transactionAt]))
               THROW 51000, 'A refund recall is immutable; retain its separate posted debit.', 1;

END

GO

CREATE TRIGGER [disputes].[tr_receipt_immutable]
    ON [disputes].[refundReceipts]
    INSTEAD OF UPDATE, DELETE
    AS THROW 51000, 'Append-only refund receipt.', 1;

GO

CREATE TRIGGER [disputes].[tr_refund_active_guard]
    ON [disputes].[refunds]
    AFTER INSERT
    AS
BEGIN
           SET NOCOUNT ON;

IF EXISTS (SELECT 1
                      FROM   inserted AS i
                             INNER JOIN
                             [disputes].[refunds] AS prior
                             ON prior.[transactionId] = i.[transactionId]
                                AND prior.[disputeId] <> i.[disputeId]
                      WHERE  NOT EXISTS (SELECT 1
                                         FROM   [disputes].[refundRecalls] AS recall
                                         WHERE  recall.[disputeId] = prior.[disputeId]))
               THROW 51000, 'The original payment already has an outstanding refund.', 1;

END

GO

CREATE TRIGGER [disputes].[tr_refund_immutable]
    ON [disputes].[refunds]
    INSTEAD OF UPDATE, DELETE
    AS THROW 51000, 'Append-only refund record.', 1;

GO

CREATE TRIGGER [disputes].[tr_refund_recall_immutable]
    ON [disputes].[refundRecalls]
    AFTER UPDATE, DELETE
    AS THROW 51000, 'Append-only refund recall record.', 1;

GO

CREATE TRIGGER [disputes].[tr_refund_recall_validate]
    ON [disputes].[refundRecalls]
    AFTER INSERT
    AS
BEGIN
           SET NOCOUNT ON;

IF EXISTS (SELECT 1
                      FROM   inserted AS i
                      WHERE  NOT EXISTS (SELECT 1
                                         FROM   [disputes].[refunds] AS r
                                                INNER JOIN
                                                [transactions].[transactions] AS credit
                                                ON credit.[id] = r.[creditTransactionId]
                                                INNER JOIN
                                                [transactions].[transactions] AS reversal
                                                ON reversal.[id] = i.[reversalTransactionId]
                                         WHERE  r.[disputeId] = i.[disputeId]
                                                AND r.[creditTransactionId] = i.[creditTransactionId]
                                                AND r.[amount] = i.[amount]
                                                AND r.[currencyCode] = i.[currencyCode]
                                                AND reversal.[accountId] = credit.[accountId]
                                                AND reversal.[amount] = credit.[amount]
                                                AND reversal.[currencyCode] = credit.[currencyCode]
                                                AND EXISTS (SELECT 1
                                                            FROM   [configuration].[transactionTypes] AS postingType
                                                            WHERE  postingType.[id] = reversal.[transactionTypeId]
                                                                   AND postingType.[code] = 'RefundReversal')
                                                AND reversal.[direction] = 'Debit'
                                                AND reversal.[transactionStatus] = 'Posted'))
               THROW 51000, 'Recall debit must exactly reverse the linked refund on its original account.', 1;

END

GO

CREATE TRIGGER [disputes].[tr_refund_validate]
    ON [disputes].[refunds]
    AFTER INSERT
    AS
BEGIN
           IF EXISTS (SELECT 1
                      FROM   inserted AS n
                      WHERE  NOT EXISTS (SELECT 1
                                         FROM   [disputes].[disputes] AS d
                                                INNER JOIN
                                                [transactions].[transactions] AS t
                                                ON t.[id] = d.[transactionId]
                                                INNER JOIN
                                                [accounts].[accounts] AS a
                                                ON a.[id] = t.[accountId]
                                                INNER JOIN
                                                [transactions].[transactions] AS c
                                                ON c.[id] = n.[creditTransactionId]
                                         WHERE  d.[id] = n.[disputeId]
                                                AND d.[customerId] = n.[customerId]
                                                AND a.[userId] = n.[customerId]
                                                AND a.[id] = n.[accountId]
                                                AND t.[id] = n.[transactionId]
                                                AND t.[direction] = 'Debit'
                                                AND t.[transactionStatus] = 'Posted'
                                                AND t.[amount] = n.[amount]
                                                AND t.[currencyCode] = n.[currencyCode]
                                                AND a.[currencyCode] = n.[currencyCode]
                                                AND c.[accountId] = a.[id]
                                                AND c.[direction] = 'Credit'
                                                AND EXISTS (SELECT 1
                                                            FROM   [configuration].[transactionTypes] AS postingType
                                                            WHERE  postingType.[id] = c.[transactionTypeId]
                                                                   AND postingType.[code] = 'Refund')
                                                AND c.[transactionStatus] = 'Posted'
                                                AND c.[amount] = n.[amount]
                                                AND c.[currencyCode] = n.[currencyCode]))
               THROW 51000, 'Refund posting does not match the owned original payment.', 1;

END

GO

CREATE TRIGGER [transactions].[tr_refunded_transaction_immutable]
    ON [transactions].[transactions]
    AFTER UPDATE
    AS
BEGIN
           IF EXISTS (SELECT 1
                      FROM   inserted AS i
                             INNER JOIN
                             deleted AS d
                             ON d.[id] = i.[id]
                      WHERE  EXISTS (SELECT 1
                                     FROM   [disputes].[refunds] AS r
                                     WHERE  r.[transactionId] = d.[id]
                                            OR r.[creditTransactionId] = d.[id])
                             AND (i.[accountId] <> d.[accountId]
                                  OR i.[amount] <> d.[amount]
                                  OR i.[currencyCode] <> d.[currencyCode]
                                  OR i.[direction] <> d.[direction]
                                  OR i.[transactionStatus] <> d.[transactionStatus]
                                  OR i.[transactionTypeId] <> d.[transactionTypeId]
                                  OR i.[transactionAt] <> d.[transactionAt]))
               THROW 51000, 'A refund-linked payment is immutable; post a separately authorised correction.', 1;

END

GO
CREATE TRIGGER configuration."tr_refundPolicies_history" ON configuration."refundPolicies" AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51068, 'Policy history cannot be physically deleted.', 1;
    IF EXISTS (
        SELECT "id", "policyId", "supersedesVersionId", "createdById", "publicId", "version", "priority", "effectiveFromUtc", "effectiveToUtc", "publishedAtUtc", "createdAtUtc", "recordedAtUtc", "changeReason", "requestId", "requestFingerprint", "sourceVersionPublicId", "provenance", "enabled", "maximumAmount", "maximumAgeHours", "dailyLimit", "duplicateWindowMinutes", "configurationKey", "initialVersion", "code", "scope" FROM inserted
        EXCEPT
        SELECT "id", "policyId", "supersedesVersionId", "createdById", "publicId", "version", "priority", "effectiveFromUtc", "effectiveToUtc", "publishedAtUtc", "createdAtUtc", "recordedAtUtc", "changeReason", "requestId", "requestFingerprint", "sourceVersionPublicId", "provenance", "enabled", "maximumAmount", "maximumAgeHours", "dailyLimit", "duplicateWindowMinutes", "configurationKey", "initialVersion", "code", "scope" FROM deleted
    )
        THROW 51068, 'Published policy content and initial definitions are immutable.', 1;
    IF EXISTS (SELECT 1 FROM inserted WHERE "version" = 0)
    BEGIN
        IF EXISTS (
            SELECT 1 FROM inserted n JOIN deleted p ON n."id" = p."id"
            WHERE n."version" <> 0 OR n."lastVersion" < p."lastVersion" OR n."concurrencyToken" <= p."concurrencyToken"
            OR n."isDeleted" <> p."isDeleted" OR n."deletedAtUtc" IS NOT NULL
            OR n."deletedById" IS NOT NULL OR n."deletionReason" IS NOT NULL
        )
            THROW 51068, 'Policy counters must advance without changing initial definitions.', 1;
    END
    ELSE IF EXISTS (
        SELECT 1 FROM inserted n JOIN deleted p ON n."id" = p."id"
        WHERE p."isDeleted" = 1 OR n."isDeleted" <> 1 OR n."deletedAtUtc" IS NULL
        OR n."deletedById" IS NULL OR n."deletionReason" IS NULL
    )
        THROW 51068, 'Policy deletion requires immutable attribution.', 1;
END
GO

CREATE TRIGGER [disputes].[tr_refundRecalls_publicIdentity]
ON [disputes].[refundRecalls]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [disputes].[tr_reinstatement_immutable]
    ON [disputes].[reinstatements]
    AFTER UPDATE, DELETE
    AS THROW 51000, 'Append-only case reinstatement record.', 1;

GO

CREATE TRIGGER [disputes].[tr_reinstatement_validate]
    ON [disputes].[reinstatements]
    AFTER INSERT
    AS
BEGIN
           SET NOCOUNT ON;

IF EXISTS (SELECT 1
                      FROM   inserted AS i
                      WHERE  NOT EXISTS (SELECT 1
                                         FROM   [disputes].[disputes] AS parent
                                                INNER JOIN
                                                [disputes].[disputes] AS child
                                                ON child.[id] = i.[childDisputeId]
                                                INNER JOIN
                                                [disputes].[refundRecalls] AS recall
                                                ON recall.[disputeId] = parent.[id]
                                         WHERE  parent.[id] = i.[parentDisputeId]
                                                AND parent.[customerId] = i.[customerId]
                                                AND child.[customerId] = parent.[customerId]
                                                AND child.[transactionId] = parent.[transactionId]
                                                AND parent.[status] IN ('Resolved', 'Declined', 'Withdrawn')
                                                AND child.[status] IN ('Submitted', 'UnderReview', 'AwaitingCustomer')))
               THROW 51000, 'A reinstated case must belong to the original customer and recalled payment.', 1;

END

GO

CREATE TRIGGER [identity].[tr_revoke_login_on_contact]
    ON [identity].[customerProfiles]
    AFTER UPDATE
    AS
BEGIN
           SET NOCOUNT ON;

UPDATE t
           SET    [revokedAt] = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')
           FROM   [identity].[trustedLoginDevices] AS t
                  INNER JOIN
                  inserted AS i
                  ON i.[userId] = t.[userId]
                  INNER JOIN
                  deleted AS d
                  ON d.[userId] = i.[userId]
           WHERE  t.[revokedAt] IS NULL
                  AND EXISTS (SELECT i.[email],
                                     i.[phone],
                                     i.[emailVerified],
                                     i.[phoneVerified]
                              EXCEPT
                              SELECT d.[email],
                                     d.[phone],
                                     d.[emailVerified],
                                     d.[phoneVerified]);

UPDATE x
           SET    [state]            = 'Failed',
                  [protectedPayload] = ''
           FROM   [identity].[identityChallenges] AS x
                  INNER JOIN
                  inserted AS i
                  ON i.[userId] = x.[userId]
                  INNER JOIN
                  deleted AS d
                  ON d.[userId] = i.[userId]
           WHERE  x.[state] IN ('Ready', 'Pending')
                  AND EXISTS (SELECT i.[email],
                                     i.[phone],
                                     i.[emailVerified],
                                     i.[phoneVerified]
                              EXCEPT
                              SELECT d.[email],
                                     d.[phone],
                                     d.[emailVerified],
                                     d.[phoneVerified]);

END

GO

CREATE TRIGGER [identity].[tr_revoke_login_on_role]
    ON [identity].[userRoles]
    AFTER INSERT, UPDATE, DELETE
    AS
BEGIN
           SET NOCOUNT ON;

UPDATE [identity].[trustedLoginDevices]
           SET    [revokedAt] = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')
           WHERE  [revokedAt] IS NULL
                  AND [userId] IN (SELECT [userId]
                                   FROM   inserted
                                   UNION
                                   SELECT [userId]
                                   FROM   deleted);

UPDATE [identity].[identityChallenges]
           SET    [state]            = 'Failed',
                  [protectedPayload] = ''
           WHERE  [state] IN ('Ready', 'Pending')
                  AND [userId] IN (SELECT [userId]
                                   FROM   inserted
                                   UNION
                                   SELECT [userId]
                                   FROM   deleted);

UPDATE [identity].[userSessions]
           SET    [revokedAt] = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')
           WHERE  [revokedAt] IS NULL
                  AND [userId] IN (SELECT [userId]
                                   FROM   inserted
                                   UNION
                                   SELECT [userId]
                                   FROM   deleted);

END

GO

CREATE TRIGGER [identity].[tr_revoke_login_on_user_security]
    ON [identity].[users]
    AFTER UPDATE
    AS
BEGIN
           SET NOCOUNT ON;

UPDATE t
           SET    [revokedAt] = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')
           FROM   [identity].[trustedLoginDevices] AS t
                  INNER JOIN
                  inserted AS i
                  ON i.[id] = t.[userId]
                  INNER JOIN
                  deleted AS d
                  ON d.[id] = i.[id]
           WHERE  t.[revokedAt] IS NULL
                  AND EXISTS (SELECT i.[passwordHash],
                                     i.[loginIdentifier],
                                     i.[accountStatus],
                                     i.[requiresSecondFactor],
                                     i.[deletedAt]
                              EXCEPT
                              SELECT d.[passwordHash],
                                     d.[loginIdentifier],
                                     d.[accountStatus],
                                     d.[requiresSecondFactor],
                                     d.[deletedAt]);

UPDATE x
           SET    [state]            = 'Failed',
                  [protectedPayload] = ''
           FROM   [identity].[identityChallenges] AS x
                  INNER JOIN
                  inserted AS i
                  ON i.[id] = x.[userId]
                  INNER JOIN
                  deleted AS d
                  ON d.[id] = i.[id]
           WHERE  x.[state] IN ('Ready', 'Pending')
                  AND EXISTS (SELECT i.[passwordHash],
                                     i.[loginIdentifier],
                                     i.[accountStatus],
                                     i.[requiresSecondFactor],
                                     i.[deletedAt]
                              EXCEPT
                              SELECT d.[passwordHash],
                                     d.[loginIdentifier],
                                     d.[accountStatus],
                                     d.[requiresSecondFactor],
                                     d.[deletedAt]);

END

GO
CREATE TRIGGER configuration."tr_riskPolicies_history" ON configuration."riskPolicies" AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51068, 'Policy history cannot be physically deleted.', 1;
    IF EXISTS (
        SELECT "id", "policyId", "supersedesVersionId", "createdById", "publicId", "version", "priority", "effectiveFromUtc", "effectiveToUtc", "publishedAtUtc", "createdAtUtc", "recordedAtUtc", "changeReason", "requestId", "requestFingerprint", "sourceVersionPublicId", "provenance", "enabled", "highValueThreshold", "extraCheckAfterDays", "debitOrderWindowDays", "debitOrderAppLimit", "configurationKey", "initialVersion", "code", "scope" FROM inserted
        EXCEPT
        SELECT "id", "policyId", "supersedesVersionId", "createdById", "publicId", "version", "priority", "effectiveFromUtc", "effectiveToUtc", "publishedAtUtc", "createdAtUtc", "recordedAtUtc", "changeReason", "requestId", "requestFingerprint", "sourceVersionPublicId", "provenance", "enabled", "highValueThreshold", "extraCheckAfterDays", "debitOrderWindowDays", "debitOrderAppLimit", "configurationKey", "initialVersion", "code", "scope" FROM deleted
    )
        THROW 51068, 'Published policy content and initial definitions are immutable.', 1;
    IF EXISTS (SELECT 1 FROM inserted WHERE "version" = 0)
    BEGIN
        IF EXISTS (
            SELECT 1 FROM inserted n JOIN deleted p ON n."id" = p."id"
            WHERE n."version" <> 0 OR n."lastVersion" < p."lastVersion" OR n."concurrencyToken" <= p."concurrencyToken"
            OR n."isDeleted" <> p."isDeleted" OR n."deletedAtUtc" IS NOT NULL
            OR n."deletedById" IS NOT NULL OR n."deletionReason" IS NOT NULL
        )
            THROW 51068, 'Policy counters must advance without changing initial definitions.', 1;
    END
    ELSE IF EXISTS (
        SELECT 1 FROM inserted n JOIN deleted p ON n."id" = p."id"
        WHERE p."isDeleted" = 1 OR n."isDeleted" <> 1 OR n."deletedAtUtc" IS NULL
        OR n."deletedById" IS NULL OR n."deletionReason" IS NULL
    )
        THROW 51068, 'Policy deletion requires immutable attribution.', 1;
END
GO

CREATE TRIGGER [identity].[tr_roles_codeIdentity]
ON [identity].[roles] AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([code]) AND EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
        WHERE CONVERT(varbinary(max), i.code) <> CONVERT(varbinary(max), d.code)
    ) THROW 51000, 'Reference codes are immutable; create a new reference entry.', 1;

END;

GO

CREATE TRIGGER [identity].[tr_security_events_immutable]
    ON [identity].[securityEvents]
    AFTER UPDATE, DELETE
    AS THROW 51000, 'Append-only security record.', 1;

GO

CREATE TRIGGER [identity].[tr_securityEvents_publicIdentity]
ON [identity].[securityEvents]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [disputes].[tr_statusHistory_publicIdentity]
ON [disputes].[statusHistory]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [disputes].[tr_statusHistory_retainContent]
ON [disputes].[statusHistory] AFTER UPDATE AS
BEGIN
    IF EXISTS(SELECT 1 FROM inserted)
        THROW 51000,'Recorded case history and messages are append-only.',1;

END;

GO

CREATE TRIGGER [disputes].[tr_statusHistory_retainRecord]
ON [disputes].[statusHistory] AFTER DELETE AS
BEGIN
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51000,'Retained business records cannot be physically deleted; use the authorized lifecycle operation.',1;

END;

GO

CREATE TRIGGER [support].[tr_support_closed_message_guard]
    ON [support].[messages]
    AFTER INSERT
    AS
BEGIN
           SET NOCOUNT ON;

IF EXISTS (SELECT 1
                      FROM   inserted AS i
                             INNER JOIN
                             [support].[conversations] AS st WITH (UPDLOCK, HOLDLOCK)
                             ON st.[id] = i.[threadId]
                      WHERE  st.[conversationStatus] = 'Closed')
               THROW 51000, 'Closed conversations cannot receive messages.', 1;

END

GO

CREATE TRIGGER [support].[tr_support_terminal_closure]
    ON [support].[conversations]
    AFTER UPDATE
    AS
BEGIN
           SET NOCOUNT ON;

IF EXISTS (SELECT 1
                      FROM   inserted AS i
                             INNER JOIN
                             deleted AS d
                             ON d.[id] = i.[id]
                      WHERE  d.[conversationStatus] = 'Closed'
                             AND (i.[conversationStatus] <> 'Closed'
                                  OR i.[closedAt] <> d.[closedAt]
                                  OR ISNULL(i.[closedById], -1) <> ISNULL(d.[closedById], -1)
                                  OR ISNULL(i.[closureReason], '') <> ISNULL(d.[closureReason], '')
                                  OR ISNULL(i.[assignedToId], -1) <> ISNULL(d.[assignedToId], -1)
                                  OR i.[concernId] <> d.[concernId]
                                  OR i.[queueType] <> d.[queueType]))
               THROW 51000, 'Closed conversations are permanently read-only.', 1;

END

GO

CREATE TRIGGER [transactions].[tr_transaction_journal]
    ON [transactions].[transactions]
    AFTER INSERT, UPDATE, DELETE
    AS
BEGIN
           SET NOCOUNT ON;

IF EXISTS (SELECT 1
                      FROM   deleted AS d
                             LEFT OUTER JOIN
                             inserted AS i
                             ON i.[id] = d.[id]
                      WHERE  i.[id] IS NULL)
               THROW 51000, 'Financial transactions cannot be deleted; post a correction.', 1;

INSERT INTO [ledger].[projectionEntries] ([accountId], [transactionId], [currentDelta], [reservedDelta], [unknownDelta], [eventKind])
           SELECT d.[accountId],
                  d.[id],
                  CASE WHEN d.[currencyCode] = a.[currencyCode]
                            AND d.[transactionStatus] = 'Posted' THEN CASE WHEN d.[direction] = 'Credit' THEN -d.[amount] ELSE d.[amount] END ELSE 0 END,
                  CASE WHEN d.[currencyCode] = a.[currencyCode]
                            AND d.[transactionStatus] = 'Pending'
                            AND d.[direction] = 'Debit' THEN -d.[amount] ELSE 0 END,
                  CASE WHEN d.[currencyCode] <> a.[currencyCode]
                            AND d.[transactionStatus] <> 'Reversed' THEN -1 ELSE 0 END,
                  'Correction'
           FROM   deleted AS d
                  INNER JOIN
                  inserted AS i
                  ON i.[id] = d.[id]
                  INNER JOIN
                  [accounts].[accounts] AS a
                  ON a.[id] = d.[accountId]
           WHERE  d.[accountId] <> i.[accountId]
                  OR d.[amount] <> i.[amount]
                  OR d.[currencyCode] <> i.[currencyCode]
                  OR d.[direction] <> i.[direction]
                  OR d.[transactionStatus] <> i.[transactionStatus];

INSERT INTO [ledger].[projectionEntries] ([accountId], [transactionId], [currentDelta], [reservedDelta], [unknownDelta], [eventKind])
           SELECT i.[accountId],
                  i.[id],
                  CASE WHEN i.[currencyCode] = a.[currencyCode]
                            AND i.[transactionStatus] = 'Posted' THEN CASE WHEN i.[direction] = 'Credit' THEN i.[amount] ELSE -i.[amount] END ELSE 0 END,
                  CASE WHEN i.[currencyCode] = a.[currencyCode]
                            AND i.[transactionStatus] = 'Pending'
                            AND i.[direction] = 'Debit' THEN i.[amount] ELSE 0 END,
                  CASE WHEN i.[currencyCode] <> a.[currencyCode]
                            AND i.[transactionStatus] <> 'Reversed' THEN 1 ELSE 0 END,
                  'Posting'
           FROM   inserted AS i
                  LEFT OUTER JOIN
                  deleted AS d
                  ON d.[id] = i.[id]
                  INNER JOIN
                  [accounts].[accounts] AS a
                  ON a.[id] = i.[accountId]
           WHERE  d.[id] IS NULL
                  OR d.[accountId] <> i.[accountId]
                  OR d.[amount] <> i.[amount]
                  OR d.[currencyCode] <> i.[currencyCode]
                  OR d.[direction] <> i.[direction]
                  OR d.[transactionStatus] <> i.[transactionStatus];

END

GO

CREATE TRIGGER [transactions].[tr_transactions_publicIdentity]
ON [transactions].[transactions]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [transactions].[tr_transactions_retainRecord]
ON [transactions].[transactions] AFTER DELETE AS
BEGIN
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51000,'Retained business records cannot be physically deleted; use the authorized lifecycle operation.',1;

END;

GO

CREATE TRIGGER [configuration].[tr_transactionTypes_codeIdentity]
ON [configuration].[transactionTypes] AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([code]) AND EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
        WHERE CONVERT(varbinary(max), i.code) <> CONVERT(varbinary(max), d.code)
    ) THROW 51000, 'Reference codes are immutable; create a new reference entry.', 1;

END;

GO

CREATE TRIGGER [configuration].[tr_transactionTypes_retainRecord]
ON [configuration].[transactionTypes] AFTER DELETE AS
BEGIN
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51000,'Retained business records cannot be physically deleted; use the authorized lifecycle operation.',1;

END;

GO

CREATE TRIGGER [identity].[tr_trustedLoginDevices_publicIdentity]
ON [identity].[trustedLoginDevices]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [identity].[tr_users_no_hard_delete]
    ON [identity].[users]
    INSTEAD OF DELETE
    AS
BEGIN
           SET NOCOUNT ON;

THROW 51000, 'Use the account closure workflow. Customer records cannot be physically deleted.', 1;

END

GO

CREATE TRIGGER [identity].[tr_users_publicIdentity]
ON [identity].[users]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE TRIGGER [identity].[tr_users_trackedRemoval]
ON [identity].[users] AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;

IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id=i.id
        WHERE d.[deletedAt] IS NOT NULL AND (i.[deletedAt] IS NULL OR i.[deletedById] IS NULL OR i.[deletedAt]<>d.[deletedAt] OR i.[deletedById]<>d.[deletedById])
    ) THROW 51000,'Removal metadata and retained records cannot be rewritten.',1;

INSERT INTO audit.[auditEvents]
        ([publicId],[actorUserId],action,[entityType],[entityPublicId],[occurredAt],[correlationId],[safeMetadata],outcome)
    SELECT NEWID(),i.[deletedById],'RecordSoftDeleted','user',i.[publicId],i.[deletedAt],CONVERT(nvarchar(36),NEWID()),
        (SELECT CAST(1 AS bit) AS retained FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),'Success'
    FROM inserted i LEFT JOIN deleted previous ON previous.id=i.id
    WHERE i.[deletedAt] IS NOT NULL AND previous.[deletedAt] IS NULL;

END;

GO

CREATE TRIGGER [identity].[tr_userSessions_publicIdentity]
ON [identity].[userSessions]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

IF UPDATE([publicId]) AND EXISTS (
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.[id]=i.[id]
        WHERE i.[publicId]<>d.[publicId]
    ) THROW 51000, 'Public identity is immutable.', 1;

END;

GO

CREATE   TRIGGER [identity].[trLoginVerificationFlowsIntegrity] ON [identity].[loginVerificationFlows] AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;

IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
        WHERE EXISTS (SELECT i.publicId, i.challengeId, i.userId EXCEPT SELECT d.publicId, d.challengeId, d.userId)
    ) THROW 51000, 'Login-flow identity and ownership are immutable.', 1;

IF EXISTS (
        SELECT 1 FROM inserted f
        WHERE NOT EXISTS (
            SELECT 1 FROM [identity].[identityChallenges] c
            WHERE c.id = f.challengeId AND c.userId = f.userId AND c.publicId = f.publicId AND c.purpose = 'Login'
        )
    ) THROW 51000, 'Invalid login-flow challenge or owner.', 1;

END;

GO

CREATE TRIGGER "disputes"."validateAutomaticDecisionsPublishedPolicy"
ON "disputes"."automaticDecisions" AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted child
        LEFT JOIN configuration."refundPolicies" policy ON policy."id" = child."refundPolicyVersionId"
        WHERE child."refundPolicyVersionId" IS NOT NULL AND (policy."id" IS NULL OR policy."version" <= 0 OR policy."publishedAtUtc" IS NULL)
    )
        THROW 51070, 'A financial policy reference must identify a published policy.', 1;
END;

GO

CREATE TRIGGER "disputes"."validateDisputesPublishedPolicy"
ON "disputes"."disputes" AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted child
        LEFT JOIN configuration."riskPolicies" policy ON policy."id" = child."riskPolicyVersionId"
        WHERE child."riskPolicyVersionId" IS NOT NULL AND (policy."id" IS NULL OR policy."version" <= 0 OR policy."publishedAtUtc" IS NULL)
    )
        THROW 51070, 'A financial policy reference must identify a published policy.', 1;
END;

GO

CREATE TRIGGER "identity"."validateDisputeVerificationsPublishedPolicy"
ON "identity"."disputeVerifications" AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted child
        LEFT JOIN configuration."riskPolicies" policy ON policy."id" = child."riskPolicyVersionId"
        WHERE child."riskPolicyVersionId" IS NOT NULL AND (policy."id" IS NULL OR policy."version" <= 0 OR policy."publishedAtUtc" IS NULL)
    )
        THROW 51070, 'A financial policy reference must identify a published policy.', 1;
END;

GO

CREATE TRIGGER "disputes"."validateFinancialCommandsPublishedPolicy"
ON "disputes"."financialCommands" AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted child
        LEFT JOIN configuration."refundPolicies" policy ON policy."id" = child."refundPolicyVersionId"
        WHERE child."refundPolicyVersionId" IS NOT NULL AND (policy."id" IS NULL OR policy."version" <= 0 OR policy."publishedAtUtc" IS NULL)
    )
        THROW 51070, 'A financial policy reference must identify a published policy.', 1;
END;

GO

CREATE TRIGGER configuration."validateRefundPoliciesReferences"
ON configuration."refundPolicies" AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted revision
        LEFT JOIN configuration."refundPolicies" family ON family."id" = revision."policyId"
        WHERE revision."version" > 0 AND (family."id" IS NULL OR family."version" <> 0)
    )
        THROW 51070, 'A policy publication must reference its family definition.', 1;

    IF EXISTS (
        SELECT 1
        FROM inserted revision
        LEFT JOIN configuration."refundPolicies" predecessor ON predecessor."id" = revision."supersedesVersionId"
        WHERE revision."supersedesVersionId" IS NOT NULL
            AND (predecessor."id" IS NULL OR predecessor."version" <= 0 OR predecessor."publishedAtUtc" IS NULL
                OR predecessor."policyId" <> revision."policyId")
    )
        THROW 51070, 'A policy predecessor must be a publication in the same family.', 1;
END;

GO

CREATE TRIGGER "disputes"."validateRefundsPublishedPolicy"
ON "disputes"."refunds" AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted child
        LEFT JOIN configuration."refundPolicies" policy ON policy."id" = child."refundPolicyVersionId"
        WHERE child."refundPolicyVersionId" IS NOT NULL AND (policy."id" IS NULL OR policy."version" <= 0 OR policy."publishedAtUtc" IS NULL)
    )
        THROW 51070, 'A financial policy reference must identify a published policy.', 1;
END;

GO

CREATE TRIGGER configuration."validateRiskPoliciesReferences"
ON configuration."riskPolicies" AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted revision
        LEFT JOIN configuration."riskPolicies" family ON family."id" = revision."policyId"
        WHERE revision."version" > 0 AND (family."id" IS NULL OR family."version" <> 0)
    )
        THROW 51070, 'A policy publication must reference its family definition.', 1;

    IF EXISTS (
        SELECT 1
        FROM inserted revision
        LEFT JOIN configuration."riskPolicies" predecessor ON predecessor."id" = revision."supersedesVersionId"
        WHERE revision."supersedesVersionId" IS NOT NULL
            AND (predecessor."id" IS NULL OR predecessor."version" <= 0 OR predecessor."publishedAtUtc" IS NULL
                OR predecessor."policyId" <> revision."policyId")
    )
        THROW 51070, 'A policy predecessor must be a publication in the same family.', 1;
END;

GO

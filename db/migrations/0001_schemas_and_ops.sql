-- ===========================================================================
-- Schemas, and the operational tables the product runs on.
--
-- Idempotent. Creates what is absent and leaves what is present alone, so it
-- can be applied to a fresh database or an existing one without checking which.
--
-- Never edit this file once it has been applied anywhere. Write a new migration
-- in the 0900 range instead.
-- ===========================================================================

-- --------------------------------------------------------------- schemas --
-- Four schemas, because the stages write to different places and the boundary
-- between them is what makes a partial run readable. Raw extraction never
-- reaches the inventory without passing through normalise, and nothing reaches
-- a client's DevOps project without passing through an approval.
IF SCHEMA_ID('ops') IS NULL EXEC('CREATE SCHEMA ops');              -- engagements, users, runs
GO
IF SCHEMA_ID('stg') IS NULL EXEC('CREATE SCHEMA stg');              -- what an extraction returned, uninterpreted
GO
IF SCHEMA_ID('inv') IS NULL EXEC('CREATE SCHEMA inv');              -- the component inventory
GO
IF SCHEMA_ID('findings') IS NULL EXEC('CREATE SCHEMA findings');    -- what the rules found, and what it would cost
GO

-- ------------------------------------------------------------ engagement --
-- One piece of client work. Everything else hangs off this, and deleting one
-- has to take its data with it, which is why the cascades below are deliberate
-- rather than accidental.
IF OBJECT_ID('ops.Engagement') IS NULL
BEGIN
    CREATE TABLE ops.Engagement
    (
        EngagementId    UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Engagement PRIMARY KEY,
        Name            NVARCHAR(200)    NOT NULL,
        ClientName      NVARCHAR(200)    NULL,
        Status          NVARCHAR(20)     NOT NULL CONSTRAINT DF_Engagement_Status DEFAULT 'active',
        -- Drives the regulatedEnvironment complexity factor. Declared per engagement
        -- rather than inferred, because inferring it from a client name is the kind of
        -- cleverness that is wrong exactly once and expensively.
        IsRegulated     BIT              NOT NULL CONSTRAINT DF_Engagement_IsRegulated DEFAULT 0,
        -- The language reports and work items are produced in. Separate from the
        -- consultant's own interface language: a Dutch consultant can be reading a
        -- Dutch screen and producing an English backlog for an offshore team.
        ReportLanguage  NVARCHAR(10)     NOT NULL CONSTRAINT DF_Engagement_ReportLanguage DEFAULT 'en',
        BacklogLanguage NVARCHAR(10)     NOT NULL CONSTRAINT DF_Engagement_BacklogLanguage DEFAULT 'en',
        CreatedUtc      DATETIME2(3)     NOT NULL CONSTRAINT DF_Engagement_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       NVARCHAR(200)    NOT NULL,
        UpdatedUtc      DATETIME2(3)     NOT NULL CONSTRAINT DF_Engagement_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_Engagement_Status CHECK (Status IN ('active', 'archived'))
    );
END
GO

-- ----------------------------------------------------------- system user --
-- One Microsoft identity admitted to the product. Never created by a sign-in
-- on its own: somebody with an account has to admit a person first, because a
-- tool that reads a client's entire solution estate is not one anybody in the
-- tenant should be able to wander into.
IF OBJECT_ID('ops.SystemUser') IS NULL
BEGIN
    CREATE TABLE ops.SystemUser
    (
        UserId          NVARCHAR(100)  NOT NULL CONSTRAINT PK_SystemUser PRIMARY KEY,
        DisplayName     NVARCHAR(200)  NOT NULL,
        Email           NVARCHAR(320)  NULL,
        IsGlobalAdmin   BIT            NOT NULL CONSTRAINT DF_SystemUser_IsGlobalAdmin DEFAULT 0,
        -- Language and theme live against the person rather than the browser, so a
        -- preference survives signing in from a second machine. The web application
        -- is not allowed to use local storage.
        Language        NVARCHAR(10)   NULL,
        Theme           NVARCHAR(10)   NULL,
        CreatedUtc      DATETIME2(3)   NOT NULL CONSTRAINT DF_SystemUser_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       NVARCHAR(200)  NOT NULL,
        UpdatedUtc      DATETIME2(3)   NOT NULL CONSTRAINT DF_SystemUser_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_SystemUser_Theme CHECK (Theme IS NULL OR Theme IN ('light', 'dark'))
    );
END
GO

-- ----------------------------------------------------- engagement access --
-- Who may see which engagement, and what they may do there. A global admin
-- bypasses this; everybody else needs a row.
--
-- The three role names are the same three the other two products in the suite
-- use. A consultant has usually used one of them already, and two products
-- calling the same level by a different name is a support conversation every
-- time somebody is granted access.
IF OBJECT_ID('ops.EngagementAccess') IS NULL
BEGIN
    CREATE TABLE ops.EngagementAccess
    (
        EngagementId    UNIQUEIDENTIFIER NOT NULL,
        UserId          NVARCHAR(100)    NOT NULL,
        Role            NVARCHAR(20)     NOT NULL,
        GrantedUtc      DATETIME2(3)     NOT NULL CONSTRAINT DF_EngagementAccess_GrantedUtc DEFAULT SYSUTCDATETIME(),
        GrantedBy       NVARCHAR(200)    NOT NULL,
        CONSTRAINT PK_EngagementAccess PRIMARY KEY (EngagementId, UserId),
        CONSTRAINT FK_EngagementAccess_Engagement FOREIGN KEY (EngagementId)
            REFERENCES ops.Engagement (EngagementId) ON DELETE CASCADE,
        CONSTRAINT FK_EngagementAccess_SystemUser FOREIGN KEY (UserId)
            REFERENCES ops.SystemUser (UserId) ON DELETE CASCADE,
        -- Only an Admin may approve a backlog. Enforced in code as well, and a role
        -- outside this set should never reach the database in the first place.
        CONSTRAINT CK_EngagementAccess_Role CHECK (Role IN ('Admin', 'Contributor', 'Viewer'))
    );
END
GO

-- -------------------------------------------------------------- connection --
-- A configured way into one system: a Power Platform environment to read, or
-- an Azure DevOps project to publish into.
--
-- No secret is ever stored here. SecretRef names a Key Vault secret and the
-- settings column holds everything else. A connection row is therefore safe to
-- read, log and export, which matters because it will be.
IF OBJECT_ID('ops.Connection') IS NULL
BEGIN
    CREATE TABLE ops.Connection
    (
        ConnectionId        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Connection PRIMARY KEY,
        EngagementId        UNIQUEIDENTIFIER NOT NULL,
        -- servicePrincipal, delegated, offlineZip or azureDevOps. The first three are
        -- the extraction modes in extraction-sources.json.
        Mode                NVARCHAR(30)     NOT NULL,
        Name                NVARCHAR(200)    NOT NULL,
        -- What the environment is for. Several rules only fire against production and
        -- guessing wrong makes a report either alarmist or useless, so it is declared
        -- rather than read off the environment name.
        EnvironmentRole     NVARCHAR(20)     NOT NULL CONSTRAINT DF_Connection_EnvironmentRole DEFAULT 'unknown',
        SettingsJson        NVARCHAR(MAX)    NOT NULL CONSTRAINT DF_Connection_SettingsJson DEFAULT '{}',
        SecretRef           NVARCHAR(400)    NULL,
        -- When the credential behind SecretRef stops working. A personal access token
        -- that expires on a Friday afternoon is the most common way a publish fails,
        -- and it is entirely predictable, so it is recorded and warned about.
        SecretExpiresUtc    DATETIME2(3)     NULL,
        LastTestedUtc       DATETIME2(3)     NULL,
        LastTestSucceeded   BIT              NULL,
        LastTestMessage     NVARCHAR(MAX)    NULL,
        -- The identity the last successful test authenticated as, and what it could
        -- read. A connection that succeeds with too few privileges fails later in a
        -- way that looks exactly like an estate with nothing in it.
        LastTestIdentity    NVARCHAR(400)    NULL,
        ReachJson           NVARCHAR(MAX)    NULL,
        CreatedUtc          DATETIME2(3)     NOT NULL CONSTRAINT DF_Connection_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy           NVARCHAR(200)    NOT NULL,
        UpdatedUtc          DATETIME2(3)     NOT NULL CONSTRAINT DF_Connection_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Connection_Engagement FOREIGN KEY (EngagementId)
            REFERENCES ops.Engagement (EngagementId) ON DELETE CASCADE,
        CONSTRAINT CK_Connection_Mode CHECK (Mode IN
            ('servicePrincipal', 'delegated', 'offlineZip', 'azureDevOps')),
        CONSTRAINT CK_Connection_EnvironmentRole CHECK (EnvironmentRole IN
            ('development', 'test', 'acceptance', 'production', 'unknown')),
        CONSTRAINT CK_Connection_Settings CHECK (ISJSON(SettingsJson) = 1),
        CONSTRAINT CK_Connection_Reach CHECK (ReachJson IS NULL OR ISJSON(ReachJson) = 1)
    );

    CREATE INDEX IX_Connection_Engagement ON ops.Connection (EngagementId, Mode);
END
GO

-- ------------------------------------------------------------------ run --
-- One pass through the pipeline. Mode decides how far it goes and whether it
-- is allowed to write anything at all.
IF OBJECT_ID('ops.AnalysisRun') IS NULL
BEGIN
    CREATE TABLE ops.AnalysisRun
    (
        RunId               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AnalysisRun PRIMARY KEY,
        EngagementId        UNIQUEIDENTIFIER NOT NULL,
        Mode                NVARCHAR(20)     NOT NULL,
        Status              NVARCHAR(30)     NOT NULL CONSTRAINT DF_AnalysisRun_Status DEFAULT 'pending',
        -- Which connection the run reads. A publish run reads nothing and writes to
        -- TargetConnectionId, and the check below stops a read mode acquiring a target
        -- by accident. The one mode that must be safe to run in a sales conversation
        -- is the one that must not be able to pick up somewhere to write.
        SourceConnectionId  UNIQUEIDENTIFIER NULL,
        TargetConnectionId  UNIQUEIDENTIFIER NULL,
        -- For a publish or a compare: the assessment this run is working from. A
        -- publish never re-analyses, because then the thing published is not the
        -- thing approved.
        BasedOnRunId        UNIQUEIDENTIFIER NULL,
        CreatedUtc          DATETIME2(3)     NOT NULL CONSTRAINT DF_AnalysisRun_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy           NVARCHAR(200)    NOT NULL,
        StartedUtc          DATETIME2(3)     NULL,
        CompletedUtc        DATETIME2(3)     NULL,
        Error               NVARCHAR(MAX)    NULL,
        CONSTRAINT FK_AnalysisRun_Engagement FOREIGN KEY (EngagementId)
            REFERENCES ops.Engagement (EngagementId) ON DELETE CASCADE,
        CONSTRAINT FK_AnalysisRun_Source FOREIGN KEY (SourceConnectionId)
            REFERENCES ops.Connection (ConnectionId),
        CONSTRAINT FK_AnalysisRun_Target FOREIGN KEY (TargetConnectionId)
            REFERENCES ops.Connection (ConnectionId),
        CONSTRAINT FK_AnalysisRun_BasedOn FOREIGN KEY (BasedOnRunId)
            REFERENCES ops.AnalysisRun (RunId),
        CONSTRAINT CK_AnalysisRun_Mode CHECK (Mode IN ('quickScan', 'assessment', 'publish', 'compare')),
        CONSTRAINT CK_AnalysisRun_Status CHECK (Status IN
            ('pending', 'running', 'awaitingApproval', 'succeeded', 'partial', 'failed', 'cancelled')),
        -- Only a publish may have somewhere to write. This is the rule that makes a
        -- quick scan safe to run against a client who has bought nothing yet.
        CONSTRAINT CK_AnalysisRun_OnlyPublishHasTarget CHECK
            (Mode = 'publish' OR TargetConnectionId IS NULL),
        -- And a publish works from an existing assessment rather than from an
        -- extraction of its own.
        CONSTRAINT CK_AnalysisRun_PublishIsDerived CHECK
            (Mode <> 'publish' OR BasedOnRunId IS NOT NULL)
    );

    CREATE INDEX IX_AnalysisRun_Engagement ON ops.AnalysisRun (EngagementId, CreatedUtc DESC);
END
GO

-- --------------------------------------------------------- run approval --
-- The gate between a backlog and a client's DevOps project.
--
-- A separate table rather than a column, so approving is an insert that cannot
-- be done by accident while updating something else, and so the absence of a
-- row is the default state rather than a flag somebody can forget to reset.
--
-- BacklogHash is what makes it meaningful. It records the exact backlog the
-- person saw. A backlog that changes afterwards no longer matches and the
-- publish refuses, because approving one thing and publishing another is the
-- failure this exists to prevent.
IF OBJECT_ID('ops.RunApproval') IS NULL
BEGIN
    CREATE TABLE ops.RunApproval
    (
        RunId           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_RunApproval PRIMARY KEY,
        BacklogHash     NVARCHAR(128)    NOT NULL,
        ItemCount       INT              NOT NULL,
        ApprovedUtc     DATETIME2(3)     NOT NULL CONSTRAINT DF_RunApproval_ApprovedUtc DEFAULT SYSUTCDATETIME(),
        ApprovedBy      NVARCHAR(100)    NOT NULL,
        ApprovedByName  NVARCHAR(200)    NOT NULL,
        CONSTRAINT FK_RunApproval_Run FOREIGN KEY (RunId)
            REFERENCES ops.AnalysisRun (RunId) ON DELETE CASCADE,
        CONSTRAINT FK_RunApproval_User FOREIGN KEY (ApprovedBy)
            REFERENCES ops.SystemUser (UserId)
    );
END
GO

-- ------------------------------------------------------------- run stage --
-- Where each stage of a run stands. Checkpointing lives here, so a run that
-- died half way through an extraction is resumed rather than restarted.
-- Re-extracting a large estate costs an hour and re-running a checker job
-- costs a place in somebody else's queue.
IF OBJECT_ID('ops.RunStage') IS NULL
BEGIN
    CREATE TABLE ops.RunStage
    (
        RunId           UNIQUEIDENTIFIER NOT NULL,
        StageId         NVARCHAR(50)     NOT NULL,
        Status          NVARCHAR(20)     NOT NULL CONSTRAINT DF_RunStage_Status DEFAULT 'pending',
        Attempt         INT              NOT NULL CONSTRAINT DF_RunStage_Attempt DEFAULT 0,
        StartedUtc      DATETIME2(3)     NULL,
        CompletedUtc    DATETIME2(3)     NULL,
        Error           NVARCHAR(MAX)    NULL,
        -- Whatever the stage needs to resume: the entity it reached, the page it
        -- was on, the checker job it is waiting for. Shape differs per stage, so
        -- it is JSON rather than columns.
        CheckpointJson  NVARCHAR(MAX)    NULL,
        CONSTRAINT PK_RunStage PRIMARY KEY (RunId, StageId),
        CONSTRAINT FK_RunStage_Run FOREIGN KEY (RunId)
            REFERENCES ops.AnalysisRun (RunId) ON DELETE CASCADE,
        CONSTRAINT CK_RunStage_Status CHECK (Status IN
            ('pending', 'running', 'succeeded', 'partial', 'failed', 'skipped', 'cancelled')),
        CONSTRAINT CK_RunStage_Checkpoint CHECK (CheckpointJson IS NULL OR ISJSON(CheckpointJson) = 1)
    );
END
GO

-- ----------------------------------------------------------- run command --
-- What the API asks the worker to do.
--
-- The API writes a row and returns. The worker claims one and does the minutes
-- or hours of work. Claiming is an update with a predicate rather than a read
-- then a write, so two workers cannot take the same command.
IF OBJECT_ID('ops.RunCommand') IS NULL
BEGIN
    CREATE TABLE ops.RunCommand
    (
        CommandId       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_RunCommand PRIMARY KEY,
        RunId           UNIQUEIDENTIFIER NOT NULL,
        Command         NVARCHAR(30)     NOT NULL,
        StageId         NVARCHAR(50)     NULL,
        RequestedUtc    DATETIME2(3)     NOT NULL CONSTRAINT DF_RunCommand_RequestedUtc DEFAULT SYSUTCDATETIME(),
        RequestedBy     NVARCHAR(200)    NOT NULL,
        ClaimedUtc      DATETIME2(3)     NULL,
        -- Which worker took it, and which image that worker is running. A Container
        -- Apps job execution keeps the image it started with, so a worker polling
        -- for hours can end up behind the deployed image while every version number
        -- agrees. Recording it here makes that visible instead of mystifying.
        ClaimedBy       NVARCHAR(200)    NULL,
        ClaimedVersion  NVARCHAR(50)     NULL,
        CompletedUtc    DATETIME2(3)     NULL,
        Succeeded       BIT              NULL,
        Error           NVARCHAR(MAX)    NULL,
        CONSTRAINT FK_RunCommand_Run FOREIGN KEY (RunId)
            REFERENCES ops.AnalysisRun (RunId) ON DELETE CASCADE,
        CONSTRAINT CK_RunCommand_Command CHECK (Command IN
            ('start', 'retryStage', 'cancel', 'approve', 'publish'))
    );

    -- The claim query reads exactly this: unclaimed commands, oldest first.
    CREATE INDEX IX_RunCommand_Unclaimed ON ops.RunCommand (RequestedUtc)
        WHERE ClaimedUtc IS NULL;
END
GO

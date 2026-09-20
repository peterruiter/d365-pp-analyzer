-- ========================================================= run selection ==
-- What a run was told to look at, and who told it.
--
-- A run used to read whatever the environment had. That is the wrong default
-- for a client estate: most of what is in a Dataverse environment is
-- Microsoft's own first party solutions, and analysing those produces a report
-- about Dynamics rather than about the client's work. It is also the slowest
-- part of a run, so reading them costs an hour to say nothing.
--
-- So a run stops after it has listed what is there and waits for a person. The
-- pause is a status rather than a separate run, because the alternative was two
-- run rows per discovery and a timeline split across them, and the question
-- "what did this report cover" then has two answers.
--
-- The absence of a row in ops.RunSelection is the default state, the same shape
-- as ops.RunApproval and for the same reason: nothing has to remember to reset
-- a flag.

-- ---------------------------------------------------- the paused status --
-- awaitingSelection joins the statuses a run can hold. The constraint is
-- dropped and rebuilt rather than extended, because a check constraint cannot
-- be altered in place.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_AnalysisRun_Status')
BEGIN
    ALTER TABLE ops.AnalysisRun DROP CONSTRAINT CK_AnalysisRun_Status;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_AnalysisRun_Status')
BEGIN
    ALTER TABLE ops.AnalysisRun ADD CONSTRAINT CK_AnalysisRun_Status CHECK (Status IN
        ('pending', 'running', 'awaitingSelection', 'awaitingApproval',
         'succeeded', 'partial', 'failed', 'cancelled'));
END
GO

-- The stage holds the same status, so the timeline says which stage is waiting
-- rather than only that the run is. Deliberately not 'succeeded': only
-- succeeded counts as completed when a run resumes, and a selectSolutions that
-- counted as done would be skipped on the way back in, leaving the selection
-- nobody had made still unapplied.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_RunStage_Status')
BEGIN
    ALTER TABLE ops.RunStage DROP CONSTRAINT CK_RunStage_Status;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_RunStage_Status')
BEGIN
    ALTER TABLE ops.RunStage ADD CONSTRAINT CK_RunStage_Status CHECK (Status IN
        ('pending', 'running', 'awaitingSelection', 'succeeded', 'partial',
         'failed', 'skipped', 'cancelled'));
END
GO

-- ------------------------------------------------------ resuming a run --
-- The command that lets a paused run carry on.
--
-- Found the way the last one was: by checking rather than assuming. The web
-- page sent a run mode the constraint did not allow, the insert failed, the
-- API threw an unhandled exception, and the button did nothing at all. The
-- same shape of mistake was one line away here.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_RunCommand_Command')
BEGIN
    ALTER TABLE ops.RunCommand DROP CONSTRAINT CK_RunCommand_Command;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_RunCommand_Command')
BEGIN
    ALTER TABLE ops.RunCommand ADD CONSTRAINT CK_RunCommand_Command CHECK (Command IN
        ('start', 'resume', 'retryStage', 'cancel', 'approve', 'publish'));
END
GO

-- -------------------------------------------- what the environment holds --
-- Every solution the run found, whether or not it was chosen. Recorded even
-- when it is not analysed, because a report covering four of nineteen
-- solutions and a report covering all nineteen look identical on the cover
-- page, and the only defence is the list of what was there.
--
-- IsSelected is nullable on purpose. NULL means nobody has answered yet, which
-- is not the same as excluded: a run resumed against a NULL selection would be
-- resuming against a question that was never asked.
IF OBJECT_ID('ops.RunSolution') IS NULL
BEGIN
    CREATE TABLE ops.RunSolution
    (
        RunId           UNIQUEIDENTIFIER NOT NULL,
        UniqueName      NVARCHAR(200)    NOT NULL,
        FriendlyName    NVARCHAR(400)    NULL,
        Version         NVARCHAR(60)     NULL,
        IsManaged       BIT              NOT NULL,
        PublisherPrefix NVARCHAR(100)    NULL,
        PublisherName   NVARCHAR(400)    NULL,
        ComponentCount  INT              NULL,
        -- Worked out when the solution is recorded rather than in the query
        -- that reads it, so the reason a box was unticked is stored next to
        -- the answer and a change to the rule does not silently rewrite
        -- history on every old run.
        IsFirstParty    BIT              NOT NULL CONSTRAINT DF_RunSolution_IsFirstParty DEFAULT 0,
        IsSelected      BIT              NULL,
        CONSTRAINT PK_RunSolution PRIMARY KEY (RunId, UniqueName),
        CONSTRAINT FK_RunSolution_Run FOREIGN KEY (RunId)
            REFERENCES ops.AnalysisRun (RunId) ON DELETE CASCADE
    );
END
GO

-- ------------------------------------------------ what a person asked for --
-- The three checks that can be turned off, and who turned them off.
--
-- Nullable, and null means "whatever the mode says". The contract owns the
-- default per mode; this table only ever records a deliberate departure from
-- it, so a mode whose default changes does not quietly keep applying the old
-- one to every run that never expressed an opinion.
--
-- Recorded against a person and a time because turning the solution checker
-- off halves the evidence behind a report, and the report should be able to
-- say who decided that.
IF OBJECT_ID('ops.RunSelection') IS NULL
BEGIN
    CREATE TABLE ops.RunSelection
    (
        RunId             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_RunSelection PRIMARY KEY,
        RunsChecker       BIT              NULL,
        ModelEstimates    BIT              NULL,
        EnvironmentHealth BIT              NULL,
        ChosenUtc         DATETIME2(3)     NOT NULL CONSTRAINT DF_RunSelection_ChosenUtc DEFAULT SYSUTCDATETIME(),
        ChosenBy          NVARCHAR(200)    NOT NULL,
        CONSTRAINT FK_RunSelection_Run FOREIGN KEY (RunId)
            REFERENCES ops.AnalysisRun (RunId) ON DELETE CASCADE
    );
END
GO

-- ------------------------------------------- where somebody was last time --
-- The engagement a person was looking at when they last used the product.
--
-- Against the person rather than the browser, like the language and the theme
-- above it, and for a stronger reason: signing in redirects through Entra and
-- comes back to a fresh page, so a browser-held selection is lost by the one
-- action most likely to precede wanting it. Every reader landed in whichever
-- engagement sorted first, which is the demonstration estate.
--
-- No foreign key, deliberately. An engagement can be deleted and access can be
-- revoked, and neither should fail somebody's next sign-in: the API checks the
-- engagement still exists and is still theirs before honouring it, and falls
-- back quietly when it is not.
IF COL_LENGTH('ops.SystemUser', 'LastEngagementId') IS NULL
BEGIN
    ALTER TABLE ops.SystemUser ADD LastEngagementId UNIQUEIDENTIFIER NULL;
END
GO

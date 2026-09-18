-- ===========================================================================
-- The component inventory: what was found, in which solution, and what it
-- refers to.
--
-- Plus the table that matters most in this product, inv.NotAssessed. It holds
-- what could not be read. Everything else in the database describes what is
-- there; that one describes the shape of the hole, and without it a report
-- built from a half-failed extraction looks exactly like a report of a clean
-- estate.
-- ===========================================================================

-- ---------------------------------------------------------- extraction run --
-- What one extraction reached, per entity. Written even when the read failed,
-- which is the whole point: a read that returned nothing and a read that was
-- refused are different facts and the arithmetic downstream depends on telling
-- them apart.
IF OBJECT_ID('stg.EntityRead') IS NULL
BEGIN
    CREATE TABLE stg.EntityRead
    (
        RunId           UNIQUEIDENTIFIER NOT NULL,
        ComponentTypeId NVARCHAR(50)     NOT NULL,
        EvidenceSource  NVARCHAR(30)     NOT NULL,
        Succeeded       BIT              NOT NULL,
        RecordCount     INT              NULL,
        -- Null on success. On failure this is what a consultant reads when a client
        -- asks why a section of the report is empty, so it carries the actual message
        -- rather than a category.
        FailureReason   NVARCHAR(MAX)    NULL,
        ReadUtc         DATETIME2(3)     NOT NULL CONSTRAINT DF_EntityRead_ReadUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_EntityRead PRIMARY KEY (RunId, ComponentTypeId, EvidenceSource),
        CONSTRAINT FK_EntityRead_Run FOREIGN KEY (RunId)
            REFERENCES ops.AnalysisRun (RunId) ON DELETE CASCADE,
        CONSTRAINT CK_EntityRead_Source CHECK (EvidenceSource IN ('metadata', 'solutionZip', 'checker', 'runtime')),
        -- A successful read has a count. A failed one has a reason. Neither may be
        -- silent, because a null count on a successful read is what turns twenty-three
        -- separate failures into an estate with nothing in it.
        CONSTRAINT CK_EntityRead_Evidence CHECK
            ((Succeeded = 1 AND RecordCount IS NOT NULL) OR (Succeeded = 0 AND FailureReason IS NOT NULL))
    );
END
GO

-- ------------------------------------------------------------- raw record --
-- What a source returned, uninterpreted. Kept so normalisation can be fixed and
-- replayed without going back to the client's environment for the data again,
-- which on a delegated connection may mean going back to a person as well.
IF OBJECT_ID('stg.RawComponent') IS NULL
BEGIN
    CREATE TABLE stg.RawComponent
    (
        RawComponentId  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_RawComponent PRIMARY KEY,
        RunId           UNIQUEIDENTIFIER NOT NULL,
        ComponentTypeId NVARCHAR(50)     NOT NULL,
        EvidenceSource  NVARCHAR(30)     NOT NULL,
        SourceKey       NVARCHAR(400)    NOT NULL,
        PayloadJson     NVARCHAR(MAX)    NOT NULL,
        CONSTRAINT FK_RawComponent_Run FOREIGN KEY (RunId)
            REFERENCES ops.AnalysisRun (RunId) ON DELETE CASCADE,
        CONSTRAINT CK_RawComponent_Payload CHECK (ISJSON(PayloadJson) = 1)
    );

    CREATE INDEX IX_RawComponent_Run ON stg.RawComponent (RunId, ComponentTypeId);
END
GO

-- --------------------------------------------------------------- solution --
IF OBJECT_ID('inv.Solution') IS NULL
BEGIN
    CREATE TABLE inv.Solution
    (
        SolutionId          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_InvSolution PRIMARY KEY,
        RunId               UNIQUEIDENTIFIER NOT NULL,
        UniqueName          NVARCHAR(200)    NOT NULL,
        FriendlyName        NVARCHAR(400)    NOT NULL,
        Version             NVARCHAR(50)     NULL,
        IsManaged           BIT              NOT NULL,
        PublisherPrefix     NVARCHAR(50)     NULL,
        PublisherName       NVARCHAR(200)    NULL,
        ComponentCount      INT              NOT NULL CONSTRAINT DF_InvSolution_ComponentCount DEFAULT 0,
        InstalledUtc        DATETIME2(3)     NULL,
        -- Whether this solution was chosen for analysis. Recorded rather than filtered,
        -- because a report covering four of nineteen solutions and one covering all
        -- nineteen look identical on the cover page.
        WasAnalysed         BIT              NOT NULL CONSTRAINT DF_InvSolution_WasAnalysed DEFAULT 1,
        CONSTRAINT FK_InvSolution_Run FOREIGN KEY (RunId)
            REFERENCES ops.AnalysisRun (RunId) ON DELETE CASCADE
    );

    CREATE INDEX IX_InvSolution_Run ON inv.Solution (RunId, WasAnalysed);
END
GO

-- -------------------------------------------------------------- component --
-- One component, normalised. The stable key is what an override and a work item
-- attach to, so it has to survive a re-extraction of the same environment.
IF OBJECT_ID('inv.Component') IS NULL
BEGIN
    CREATE TABLE inv.Component
    (
        ComponentId     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Component PRIMARY KEY,
        RunId           UNIQUEIDENTIFIER NOT NULL,
        SolutionId      UNIQUEIDENTIFIER NULL,
        ComponentTypeId NVARCHAR(50)     NOT NULL,
        -- Derived from the component type and the platform's own identifier for the
        -- component, never from a display name. A rename must not orphan an estimate
        -- somebody spent a workshop agreeing.
        StableKey       NVARCHAR(400)    NOT NULL,
        PlatformId      NVARCHAR(200)    NULL,
        DisplayName     NVARCHAR(400)    NOT NULL,
        SchemaName      NVARCHAR(200)    NULL,
        -- Copied from the catalogue at normalise time rather than joined at read time.
        -- The catalogue moves between releases, and a report has to keep saying what it
        -- said on the day it was issued.
        Craft           NVARCHAR(20)     NOT NULL,
        Lifecycle       NVARCHAR(20)     NOT NULL,
        Domain          NVARCHAR(30)     NOT NULL,
        IsManaged       BIT              NOT NULL CONSTRAINT DF_Component_IsManaged DEFAULT 0,
        IsCustom        BIT              NOT NULL CONSTRAINT DF_Component_IsCustom DEFAULT 1,
        OwnerUpn        NVARCHAR(320)    NULL,
        -- Everything the component type declares in its attributes list. Shape differs
        -- per type, so JSON rather than a hundred sparse columns.
        AttributesJson  NVARCHAR(MAX)    NOT NULL CONSTRAINT DF_Component_AttributesJson DEFAULT '{}',
        CONSTRAINT FK_Component_Run FOREIGN KEY (RunId)
            REFERENCES ops.AnalysisRun (RunId) ON DELETE CASCADE,
        CONSTRAINT FK_Component_Solution FOREIGN KEY (SolutionId)
            REFERENCES inv.Solution (SolutionId),
        CONSTRAINT CK_Component_Craft CHECK (Craft IN ('config', 'lowCode', 'proCode', 'external', 'content')),
        CONSTRAINT CK_Component_Lifecycle CHECK (Lifecycle IN ('current', 'dated', 'deprecated', 'removed', 'preview')),
        CONSTRAINT CK_Component_Attributes CHECK (ISJSON(AttributesJson) = 1),
        -- Normalising twice updates rather than duplicating. Without this a replayed
        -- normalisation doubles every count in the report and nothing complains.
        CONSTRAINT UQ_Component_RunStableKey UNIQUE (RunId, StableKey)
    );

    CREATE INDEX IX_Component_RunType ON inv.Component (RunId, ComponentTypeId);
    CREATE INDEX IX_Component_RunCraft ON inv.Component (RunId, Craft, Lifecycle);
END
GO

-- -------------------------------------------------------------- reference --
-- Which component points at which. Built by the resolve stage and needed by
-- most of the interesting rules: a column on no form, a script on five forms,
-- a flow with no connection reference.
IF OBJECT_ID('inv.ComponentReference') IS NULL
BEGIN
    CREATE TABLE inv.ComponentReference
    (
        FromComponentId UNIQUEIDENTIFIER NOT NULL,
        ToComponentId   UNIQUEIDENTIFIER NOT NULL,
        Kind            NVARCHAR(50)     NOT NULL,
        RunId           UNIQUEIDENTIFIER NOT NULL,
        CONSTRAINT PK_ComponentReference PRIMARY KEY (FromComponentId, ToComponentId, Kind),
        CONSTRAINT FK_ComponentReference_From FOREIGN KEY (FromComponentId)
            REFERENCES inv.Component (ComponentId) ON DELETE CASCADE,
        CONSTRAINT FK_ComponentReference_To FOREIGN KEY (ToComponentId)
            REFERENCES inv.Component (ComponentId)
    );

    CREATE INDEX IX_ComponentReference_To ON inv.ComponentReference (RunId, ToComponentId);
END
GO

-- ---------------------------------------------------------- unresolved ref --
-- A reference to something that is not in the inventory. Usually a component
-- outside the analysed solutions, occasionally a component that does not exist.
-- Kept separately rather than dropped, because "this flow calls a child flow
-- that is in no solution" is a finding and a dropped row is not.
IF OBJECT_ID('inv.UnresolvedReference') IS NULL
BEGIN
    CREATE TABLE inv.UnresolvedReference
    (
        UnresolvedReferenceId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_UnresolvedReference PRIMARY KEY,
        RunId                 UNIQUEIDENTIFIER NOT NULL,
        FromComponentId       UNIQUEIDENTIFIER NOT NULL,
        Kind                  NVARCHAR(50)     NOT NULL,
        TargetDescription     NVARCHAR(400)    NOT NULL,
        CONSTRAINT FK_UnresolvedReference_Run FOREIGN KEY (RunId)
            REFERENCES ops.AnalysisRun (RunId) ON DELETE CASCADE,
        CONSTRAINT FK_UnresolvedReference_From FOREIGN KEY (FromComponentId)
            REFERENCES inv.Component (ComponentId) ON DELETE CASCADE
    );
END
GO

-- ------------------------------------------------------------ not assessed --
-- The most important table in this database.
--
-- One row per rule that could not run, with the reason. A report reads from
-- here and names them. Nothing anywhere in this product is allowed to report a
-- rule as passing when its evidence could not be reached, and this table is how
-- that promise is kept rather than remembered.
IF OBJECT_ID('inv.NotAssessed') IS NULL
BEGIN
    CREATE TABLE inv.NotAssessed
    (
        RunId           UNIQUEIDENTIFIER NOT NULL,
        RuleId          NVARCHAR(100)    NOT NULL,
        Reason          NVARCHAR(MAX)    NOT NULL,
        MissingEvidence NVARCHAR(200)    NULL,
        CONSTRAINT PK_NotAssessed PRIMARY KEY (RunId, RuleId),
        CONSTRAINT FK_NotAssessed_Run FOREIGN KEY (RunId)
            REFERENCES ops.AnalysisRun (RunId) ON DELETE CASCADE
    );
END
GO

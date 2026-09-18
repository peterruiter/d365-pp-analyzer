-- ===========================================================================
-- What the rules found, what it would cost, and what that becomes in a backlog.
--
-- The estimate tables enforce two rules from estimate-model.json that are too
-- important to leave to code: every estimate is a range, and every estimate has
-- a rationale. Both are constraints here, so a code path that forgets one fails
-- at the insert rather than in a client's report.
-- ===========================================================================

-- ---------------------------------------------------------------- finding --
IF OBJECT_ID('findings.Finding') IS NULL
BEGIN
    CREATE TABLE findings.Finding
    (
        FindingId       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Finding PRIMARY KEY,
        RunId           UNIQUEIDENTIFIER NOT NULL,
        EngagementId    UNIQUEIDENTIFIER NOT NULL,
        RuleId          NVARCHAR(100)    NOT NULL,
        -- Null for a solution wide rule. Most findings name a component, and the ones
        -- that do not are the ones that outrank everything else in the report.
        ComponentId     UNIQUEIDENTIFIER NULL,
        -- Carried forward so an override survives a re-extraction. Matching on the
        -- FindingId would mean re-typing forty corrections after every run, which is
        -- the same as having no override table at all.
        StableKey       NVARCHAR(400)    NOT NULL,
        Severity        NVARCHAR(20)     NOT NULL,
        Category        NVARCHAR(30)     NOT NULL,
        -- Where the rule came from. 'catalogue' for this product's own rules,
        -- 'checker' for anything the Power Apps checker returned, and in that case
        -- CheckerRuleId carries Microsoft's identifier so a consultant can look it up.
        Origin          NVARCHAR(20)     NOT NULL CONSTRAINT DF_Finding_Origin DEFAULT 'catalogue',
        CheckerRuleId   NVARCHAR(100)    NULL,
        -- What actually triggered it. A recommendation nobody can check is one nobody
        -- will act on, so this is not optional.
        EvidenceJson    NVARCHAR(MAX)    NOT NULL,
        DetectedUtc     DATETIME2(3)     NOT NULL CONSTRAINT DF_Finding_DetectedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Finding_Run FOREIGN KEY (RunId)
            REFERENCES ops.AnalysisRun (RunId) ON DELETE CASCADE,
        CONSTRAINT FK_Finding_Engagement FOREIGN KEY (EngagementId)
            REFERENCES ops.Engagement (EngagementId),
        CONSTRAINT FK_Finding_Component FOREIGN KEY (ComponentId)
            REFERENCES inv.Component (ComponentId),
        CONSTRAINT CK_Finding_Severity CHECK (Severity IN ('critical', 'high', 'medium', 'low', 'info')),
        CONSTRAINT CK_Finding_Origin CHECK (Origin IN ('catalogue', 'checker')),
        CONSTRAINT CK_Finding_Evidence CHECK (ISJSON(EvidenceJson) = 1),
        CONSTRAINT CK_Finding_CheckerId CHECK (Origin <> 'checker' OR CheckerRuleId IS NOT NULL),
        CONSTRAINT UQ_Finding_RunStableKey UNIQUE (RunId, StableKey)
    );

    CREATE INDEX IX_Finding_RunRule ON findings.Finding (RunId, RuleId);
    CREATE INDEX IX_Finding_RunSeverity ON findings.Finding (RunId, Severity, Category);
    -- The override match. Engagement rather than run, because that is the whole point.
    CREATE INDEX IX_Finding_EngagementKey ON findings.Finding (EngagementId, StableKey);
END
GO

-- --------------------------------------------------------------- estimate --
-- One per finding. Layer says which of the three produced it, and the
-- constraints below hold whichever it was.
IF OBJECT_ID('findings.Estimate') IS NULL
BEGIN
    CREATE TABLE findings.Estimate
    (
        FindingId       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Estimate PRIMARY KEY,
        RunId           UNIQUEIDENTIFIER NOT NULL,
        -- engagementOverride, model or bandDefault. Precedence is decided in code and
        -- recorded here, so a number in a report can be traced to what produced it.
        Layer           NVARCHAR(20)     NOT NULL,
        -- Two columns, never one. The schema has nowhere to put a point estimate,
        -- which is the ranges-only rule enforced rather than written down.
        LowHours        DECIMAL(10, 2)   NOT NULL,
        HighHours       DECIMAL(10, 2)   NOT NULL,
        StoryPoints     INT              NULL,
        Confidence      NVARCHAR(10)     NOT NULL,
        -- Not nullable, and checked for emptiness as well. This is what goes in the
        -- work item description and what gets read aloud in a room. An estimate whose
        -- rationale is an empty string is one nobody can defend three months later.
        Rationale       NVARCHAR(MAX)    NOT NULL,
        AssumptionsJson NVARCHAR(MAX)    NULL,
        -- Set when the estimate fell outside the band anchor or failed a sanity check.
        -- Reported rather than suppressed: a model that produced something implausible
        -- is information, not an error to hide.
        FlaggedReason   NVARCHAR(400)    NULL,
        CreatedUtc      DATETIME2(3)     NOT NULL CONSTRAINT DF_Estimate_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Estimate_Finding FOREIGN KEY (FindingId)
            REFERENCES findings.Finding (FindingId) ON DELETE CASCADE,
        CONSTRAINT FK_Estimate_Run FOREIGN KEY (RunId)
            REFERENCES ops.AnalysisRun (RunId),
        CONSTRAINT CK_Estimate_Layer CHECK (Layer IN ('engagementOverride', 'model', 'bandDefault')),
        CONSTRAINT CK_Estimate_Confidence CHECK (Confidence IN ('high', 'medium', 'low')),
        CONSTRAINT CK_Estimate_Range CHECK (LowHours <= HighHours),
        CONSTRAINT CK_Estimate_Rationale CHECK (LEN(LTRIM(RTRIM(Rationale))) > 0),
        CONSTRAINT CK_Estimate_Assumptions CHECK (AssumptionsJson IS NULL OR ISJSON(AssumptionsJson) = 1),
        CONSTRAINT CK_Estimate_Points CHECK (StoryPoints IS NULL OR StoryPoints IN (1, 2, 3, 5, 8, 13, 21))
    );
END
GO

-- ---------------------------------------------------- model call provenance --
-- Everything needed to explain why a number changed between two runs. Separate
-- from the estimate so a band default or an override carries no empty columns,
-- and so a model call that was rejected still leaves a trace.
IF OBJECT_ID('findings.ModelCall') IS NULL
BEGIN
    CREATE TABLE findings.ModelCall
    (
        ModelCallId         UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ModelCall PRIMARY KEY,
        FindingId           UNIQUEIDENTIFIER NOT NULL,
        Model               NVARCHAR(100)    NOT NULL,
        PromptVersion       NVARCHAR(20)     NOT NULL,
        PromptHash          NVARCHAR(128)    NOT NULL,
        FindingPayloadHash  NVARCHAR(128)    NOT NULL,
        Temperature         DECIMAL(3, 2)    NOT NULL,
        TokensIn            INT              NULL,
        TokensOut           INT              NULL,
        Accepted            BIT              NOT NULL,
        RejectionReason     NVARCHAR(400)    NULL,
        RequestedUtc        DATETIME2(3)     NOT NULL CONSTRAINT DF_ModelCall_RequestedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ModelCall_Finding FOREIGN KEY (FindingId)
            REFERENCES findings.Finding (FindingId) ON DELETE CASCADE,
        CONSTRAINT CK_ModelCall_Rejection CHECK (Accepted = 1 OR RejectionReason IS NOT NULL)
    );

    CREATE INDEX IX_ModelCall_Finding ON findings.ModelCall (FindingId, RequestedUtc DESC);
END
GO

-- ------------------------------------------------------ engagement override --
-- The layer that beats the model. Lives against the engagement rather than the
-- run, which is what lets it survive a re-extraction.
IF OBJECT_ID('findings.EngagementOverride') IS NULL
BEGIN
    CREATE TABLE findings.EngagementOverride
    (
        OverrideId      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_EngagementOverride PRIMARY KEY,
        EngagementId    UNIQUEIDENTIFIER NOT NULL,
        Scope           NVARCHAR(30)     NOT NULL,
        RuleId          NVARCHAR(100)    NULL,
        ComponentTypeId NVARCHAR(50)     NULL,
        -- The finding's stable key, not its id. A finding id changes every run and an
        -- override keyed on one would last exactly until the next extraction.
        FindingKey      NVARCHAR(400)    NULL,
        LowHours        DECIMAL(10, 2)   NOT NULL,
        HighHours       DECIMAL(10, 2)   NOT NULL,
        StoryPoints     INT              NULL,
        Rationale       NVARCHAR(MAX)    NOT NULL,
        SetBy           NVARCHAR(100)    NOT NULL,
        SetUtc          DATETIME2(3)     NOT NULL CONSTRAINT DF_EngagementOverride_SetUtc DEFAULT SYSUTCDATETIME(),
        -- Where this was copied from, when it was copied from another engagement.
        -- Never automatic, and recorded when it happens, because one client's numbers
        -- silently becoming another client's numbers is not a thing to leave to a default.
        CopiedFromEngagementId UNIQUEIDENTIFIER NULL,
        CONSTRAINT FK_EngagementOverride_Engagement FOREIGN KEY (EngagementId)
            REFERENCES ops.Engagement (EngagementId) ON DELETE CASCADE,
        CONSTRAINT FK_EngagementOverride_User FOREIGN KEY (SetBy)
            REFERENCES ops.SystemUser (UserId),
        CONSTRAINT CK_EngagementOverride_Scope CHECK (Scope IN ('rule', 'ruleAndComponentType', 'finding')),
        CONSTRAINT CK_EngagementOverride_Range CHECK (LowHours <= HighHours),
        CONSTRAINT CK_EngagementOverride_Rationale CHECK (LEN(LTRIM(RTRIM(Rationale))) > 0),
        CONSTRAINT CK_EngagementOverride_Points CHECK (StoryPoints IS NULL OR StoryPoints IN (1, 2, 3, 5, 8, 13, 21)),
        -- Each scope needs its own key columns and nothing else, so a finding scoped
        -- override cannot quietly be missing the finding it applies to.
        CONSTRAINT CK_EngagementOverride_Keys CHECK
        (
            (Scope = 'rule' AND RuleId IS NOT NULL AND ComponentTypeId IS NULL AND FindingKey IS NULL)
            OR (Scope = 'ruleAndComponentType' AND RuleId IS NOT NULL AND ComponentTypeId IS NOT NULL AND FindingKey IS NULL)
            OR (Scope = 'finding' AND FindingKey IS NOT NULL)
        )
    );

    CREATE INDEX IX_EngagementOverride_Lookup
        ON findings.EngagementOverride (EngagementId, Scope, RuleId, ComponentTypeId);
END
GO

-- ------------------------------------------------------------------ score --
-- The numbers on the front page. Stored rather than recomputed for the same
-- reason the sibling product stores its assessment: a figure that quietly
-- changes because a catalogue was retuned three weeks later is a figure nobody
-- can be held to, and the number in a statement of work has to be the number
-- the tool produced on the day.
IF OBJECT_ID('findings.RunScore') IS NULL
BEGIN
    CREATE TABLE findings.RunScore
    (
        RunId               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_RunScore PRIMARY KEY,
        EngagementId        UNIQUEIDENTIFIER NOT NULL,
        SolutionsAnalysed   INT              NOT NULL,
        SolutionsTotal      INT              NOT NULL,
        ComponentsTotal     INT              NOT NULL,
        LowCodeCount        INT              NOT NULL,
        ProCodeCount        INT              NOT NULL,
        ExternalCount       INT              NOT NULL,
        ConfigCount         INT              NOT NULL,
        ContentCount        INT              NOT NULL,
        AgeingCount         INT              NOT NULL,
        FindingsTotal       INT              NOT NULL,
        CriticalCount       INT              NOT NULL,
        HighCount           INT              NOT NULL,
        NotAssessedCount    INT              NOT NULL,
        TotalLowHours       DECIMAL(10, 2)   NOT NULL,
        TotalHighHours      DECIMAL(10, 2)   NOT NULL,
        FixedCostLowHours   DECIMAL(10, 2)   NOT NULL,
        FixedCostHighHours  DECIMAL(10, 2)   NOT NULL,
        -- Per domain and per category breakdowns, and the ratio definition text as it
        -- stood on the day. The definition travels with the number into every export,
        -- because the number gets quoted without it.
        BreakdownJson       NVARCHAR(MAX)    NOT NULL,
        CreatedUtc          DATETIME2(3)     NOT NULL CONSTRAINT DF_RunScore_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_RunScore_Run FOREIGN KEY (RunId)
            REFERENCES ops.AnalysisRun (RunId) ON DELETE CASCADE,
        CONSTRAINT FK_RunScore_Engagement FOREIGN KEY (EngagementId)
            REFERENCES ops.Engagement (EngagementId),
        CONSTRAINT CK_RunScore_Breakdown CHECK (ISJSON(BreakdownJson) = 1),
        CONSTRAINT CK_RunScore_Range CHECK (TotalLowHours <= TotalHighHours),
        CONSTRAINT CK_RunScore_FixedRange CHECK (FixedCostLowHours <= FixedCostHighHours)
    );

    CREATE INDEX IX_RunScore_Engagement ON findings.RunScore (EngagementId, CreatedUtc DESC);
END
GO

-- ----------------------------------------------------------- backlog item --
-- What would be created in Azure DevOps. Built before anybody approves anything
-- and exported whether or not anybody publishes, because most engagements stop
-- at the workbook.
IF OBJECT_ID('findings.BacklogItem') IS NULL
BEGIN
    CREATE TABLE findings.BacklogItem
    (
        BacklogItemId       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_BacklogItem PRIMARY KEY,
        RunId               UNIQUEIDENTIFIER NOT NULL,
        EngagementId        UNIQUEIDENTIFIER NOT NULL,
        ParentItemId        UNIQUEIDENTIFIER NULL,
        WorkItemType        NVARCHAR(20)     NOT NULL,
        Title               NVARCHAR(255)    NOT NULL,
        DescriptionHtml     NVARCHAR(MAX)    NOT NULL,
        AcceptanceCriteria  NVARCHAR(MAX)    NOT NULL,
        TestRequirement     NVARCHAR(MAX)    NOT NULL,
        Priority            INT              NOT NULL,
        StoryPoints         INT              NULL,
        LowHours            DECIMAL(10, 2)   NULL,
        HighHours           DECIMAL(10, 2)   NULL,
        TagsJson            NVARCHAR(MAX)    NOT NULL,
        -- Derived from the engagement, the rule and the component. What makes a second
        -- publish update rather than duplicate.
        DeterministicKey    NVARCHAR(200)    NOT NULL,
        CONSTRAINT FK_BacklogItem_Run FOREIGN KEY (RunId)
            REFERENCES ops.AnalysisRun (RunId) ON DELETE CASCADE,
        CONSTRAINT FK_BacklogItem_Engagement FOREIGN KEY (EngagementId)
            REFERENCES ops.Engagement (EngagementId),
        CONSTRAINT FK_BacklogItem_Parent FOREIGN KEY (ParentItemId)
            REFERENCES findings.BacklogItem (BacklogItemId),
        CONSTRAINT CK_BacklogItem_Type CHECK (WorkItemType IN ('epic', 'feature', 'story', 'task', 'bug')),
        CONSTRAINT CK_BacklogItem_Tags CHECK (ISJSON(TagsJson) = 1),
        CONSTRAINT CK_BacklogItem_Range CHECK (LowHours IS NULL OR HighHours IS NULL OR LowHours <= HighHours),
        -- Acceptance criteria are not optional. A work item generated by a tool with no
        -- acceptance criteria gets closed by somebody deciding it was fine.
        CONSTRAINT CK_BacklogItem_Criteria CHECK (LEN(LTRIM(RTRIM(AcceptanceCriteria))) > 0),
        CONSTRAINT UQ_BacklogItem_RunKey UNIQUE (RunId, DeterministicKey)
    );

    CREATE INDEX IX_BacklogItem_Run ON findings.BacklogItem (RunId, WorkItemType);
END
GO

-- ----------------------------------------------- which findings are in which --
IF OBJECT_ID('findings.BacklogItemFinding') IS NULL
BEGIN
    CREATE TABLE findings.BacklogItemFinding
    (
        BacklogItemId   UNIQUEIDENTIFIER NOT NULL,
        FindingId       UNIQUEIDENTIFIER NOT NULL,
        CONSTRAINT PK_BacklogItemFinding PRIMARY KEY (BacklogItemId, FindingId),
        CONSTRAINT FK_BacklogItemFinding_Item FOREIGN KEY (BacklogItemId)
            REFERENCES findings.BacklogItem (BacklogItemId) ON DELETE CASCADE,
        CONSTRAINT FK_BacklogItemFinding_Finding FOREIGN KEY (FindingId)
            REFERENCES findings.Finding (FindingId) ON DELETE CASCADE
    );
END
GO

-- -------------------------------------------------------- publish record --
-- What was created, where, and by which run. So a publish that went into the
-- wrong project can be found and removed rather than hunted.
IF OBJECT_ID('findings.PublishedWorkItem') IS NULL
BEGIN
    CREATE TABLE findings.PublishedWorkItem
    (
        PublishedWorkItemId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PublishedWorkItem PRIMARY KEY,
        RunId               UNIQUEIDENTIFIER NOT NULL,
        BacklogItemId       UNIQUEIDENTIFIER NOT NULL,
        Organisation        NVARCHAR(200)    NOT NULL,
        Project             NVARCHAR(200)    NOT NULL,
        WorkItemId          INT              NOT NULL,
        Url                 NVARCHAR(1000)   NOT NULL,
        Action              NVARCHAR(20)     NOT NULL,
        PublishedUtc        DATETIME2(3)     NOT NULL CONSTRAINT DF_PublishedWorkItem_PublishedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_PublishedWorkItem_Run FOREIGN KEY (RunId)
            REFERENCES ops.AnalysisRun (RunId) ON DELETE CASCADE,
        CONSTRAINT FK_PublishedWorkItem_Backlog FOREIGN KEY (BacklogItemId)
            REFERENCES findings.BacklogItem (BacklogItemId),
        CONSTRAINT CK_PublishedWorkItem_Action CHECK (Action IN ('created', 'updated', 'commented', 'skipped'))
    );

    CREATE INDEX IX_PublishedWorkItem_Run ON findings.PublishedWorkItem (RunId);
END
GO

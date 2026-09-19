-- ============================================================= narrative ==
-- The half of an assessment that comes from talking to people.
--
-- Five sections of the report model are written by a consultant rather than
-- generated: the management summary, delivery and ALM, functional maturity,
-- readiness for change and the scenarios. Nothing in an estate produces them
-- and nothing ever will. Until this table existed the report printed the
-- prompt where the text should be, which was honest and not finished.
--
-- Keyed on the engagement rather than on the run, deliberately. A run is one
-- reading of an estate and there will be several; what somebody learned in a
-- workshop is about the client and survives every re-extraction. Keying this
-- on the run would throw the written half away every time the generated half
-- was refreshed, which is the opposite of the right behaviour.
--
-- The section id comes from report-model.json and is not constrained here.
-- The contract owns which sections exist, a check constraint would be a second
-- copy of that list, and the two would disagree the first time a section was
-- added.
IF OBJECT_ID('findings.ReportNarrative') IS NULL
BEGIN
    CREATE TABLE findings.ReportNarrative
    (
        EngagementId  UNIQUEIDENTIFIER NOT NULL,
        SectionId     NVARCHAR(60)     NOT NULL,
        Body          NVARCHAR(MAX)    NOT NULL,
        UpdatedUtc    DATETIME2(3)     NOT NULL CONSTRAINT DF_ReportNarrative_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        UpdatedBy     NVARCHAR(100)    NOT NULL,
        -- The name as well as the identifier. A report that says who wrote a
        -- section is more useful than one that says which sign-in name did, and
        -- resolving it at read time means a deleted user blanks the byline.
        UpdatedByName NVARCHAR(200)    NOT NULL,
        CONSTRAINT PK_ReportNarrative PRIMARY KEY (EngagementId, SectionId),
        CONSTRAINT FK_ReportNarrative_Engagement FOREIGN KEY (EngagementId)
            REFERENCES ops.Engagement (EngagementId) ON DELETE CASCADE,
        CONSTRAINT FK_ReportNarrative_User FOREIGN KEY (UpdatedBy)
            REFERENCES ops.SystemUser (UserId),
        -- An empty section is an absent section. Storing a row of whitespace
        -- would make the report print nothing where it should print the prompt,
        -- and a reader could not tell the difference between not written and
        -- deliberately left blank.
        CONSTRAINT CK_ReportNarrative_Body CHECK (LEN(LTRIM(RTRIM(Body))) > 0)
    );
END
GO

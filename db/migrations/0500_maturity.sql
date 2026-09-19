-- ============================================================== maturity ==
-- The functional maturity scores, per capability axis.
--
-- Sixteen axes in four groups, declared in report-model.json and scored nought
-- to five. Entirely a consultant's judgement from interviews and
-- demonstrations: no metadata anywhere says whether campaign management is any
-- good, and the product's job is to hold the number somebody entered and draw
-- it, never to infer one.
--
-- Keyed on the engagement, like the narrative, and for the same reason: what
-- somebody learned in a workshop is about the client and survives every
-- re-extraction of their estate.
--
-- The axis id is not constrained here. The contract owns which axes exist and a
-- check constraint would be a second copy of that list, disagreeing with the
-- first the day somebody adds an axis.
IF OBJECT_ID('findings.MaturityScore') IS NULL
BEGIN
    CREATE TABLE findings.MaturityScore
    (
        EngagementId  UNIQUEIDENTIFIER NOT NULL,
        AxisId        NVARCHAR(60)     NOT NULL,
        Score         DECIMAL(3, 1)    NOT NULL,
        -- Who told you. The prompt asks for it per axis, because a score with no
        -- provenance is the most quotable number in the report and the easiest
        -- to have made up.
        Evidence      NVARCHAR(MAX)    NULL,
        UpdatedUtc    DATETIME2(3)     NOT NULL CONSTRAINT DF_MaturityScore_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        UpdatedBy     NVARCHAR(100)    NOT NULL,
        UpdatedByName NVARCHAR(200)    NOT NULL,
        CONSTRAINT PK_MaturityScore PRIMARY KEY (EngagementId, AxisId),
        CONSTRAINT FK_MaturityScore_Engagement FOREIGN KEY (EngagementId)
            REFERENCES ops.Engagement (EngagementId) ON DELETE CASCADE,
        CONSTRAINT FK_MaturityScore_User FOREIGN KEY (UpdatedBy)
            REFERENCES ops.SystemUser (UserId),
        -- The scale is nought to five and the chart prints the scale beside the
        -- shape. A six would draw outside the grid and a negative would draw
        -- through the middle of it.
        CONSTRAINT CK_MaturityScore_Range CHECK (Score >= 0 AND Score <= 5)
    );
END
GO

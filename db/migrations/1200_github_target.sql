-- ================================================== github as a publish target ==
-- The same defect 0700 recorded for Jira, caught the same way and before anybody
-- could hit it.
--
-- The API offers github in PublishTargets() and treats it as a target throughout.
-- CK_Connection_Mode allowed five values that did not include it, so creating a
-- GitHub connection would have failed on the constraint, thrown, and presented as
-- a wizard whose last step does nothing.
--
-- There is a guard test over this now: Every_literal_written_to_a_constrained_column
-- reads PublishTargets() and holds it against the constraint in this file, so a
-- seventh mode that forgets this migration fails in the test run rather than in
-- front of a consultant.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Connection_Mode')
BEGIN
    ALTER TABLE ops.[Connection] DROP CONSTRAINT CK_Connection_Mode;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Connection_Mode')
BEGIN
    ALTER TABLE ops.[Connection] ADD CONSTRAINT CK_Connection_Mode CHECK (Mode IN
        ('servicePrincipal', 'delegated', 'offlineZip', 'azureDevOps', 'jira', 'github'));
END
GO

-- ------------------------------------------------------- what was published where --
-- findings.PublishedWorkItem.WorkItemId is an integer, which is what Azure DevOps
-- work item identifiers and GitHub issue numbers both are. Jira issue keys are not,
-- and that column already stores nought for them with the key in the URL beside it.
--
-- Nothing to change, then. This note exists because the obvious reading of a new
-- target is that it needs a column, and the next person to add one should know the
-- question was asked rather than missed.
GO

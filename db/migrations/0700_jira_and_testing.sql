-- ============================================== jira, and proving a connection ==
-- Two things, both the same shape as defects this file has recorded before.
--
-- A Jira connection could not be stored. The API offers jira in its list of
-- connection modes and treats it as a publish target throughout, and
-- CK_Connection_Mode allowed four values that did not include it. Creating one
-- would have failed on the constraint, thrown, and presented as a wizard that
-- does nothing on its last step, which is exactly how the Discover button
-- behaved for the whole of its life.
--
-- Found by reading the constraint rather than by anybody trying it, because
-- there is no live Jira to try it against.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Connection_Mode')
BEGIN
    ALTER TABLE ops.[Connection] DROP CONSTRAINT CK_Connection_Mode;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Connection_Mode')
BEGIN
    ALTER TABLE ops.[Connection] ADD CONSTRAINT CK_Connection_Mode CHECK (Mode IN
        ('servicePrincipal', 'delegated', 'offlineZip', 'azureDevOps', 'jira'));
END
GO

-- ------------------------------------------------- when somebody last proved it --
-- The connections screen has always said "not tested" or "last tested", and
-- nothing could ever set it except a run: RecordConnectionTestAsync is called
-- from inside the extract stage. So a publish target could not be tested at
-- all, and a source could only be tested by analysing an estate with it.
--
-- The columns for the answer already exist. What was missing was a way to ask,
-- which is an endpoint rather than a table, so nothing is added here. This
-- comment is the record of why the columns were empty rather than unused.
--
-- LastTestedBy is added because a test is now something a person does on
-- purpose, and a green tick with no name beside it is worth less than one with.
IF COL_LENGTH('ops.[Connection]', 'LastTestedBy') IS NULL
BEGIN
    ALTER TABLE ops.[Connection] ADD LastTestedBy NVARCHAR(200) NULL;
END
GO

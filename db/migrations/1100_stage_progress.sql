-- ============================================ what a stage is doing now ==
-- The run screen polled every five seconds and learned nothing between one
-- stage finishing and the next starting. A stage that reads a client's estate
-- takes minutes, and for all of them the screen said "running" and the elapsed
-- time went up: a slow export and a hung worker look exactly the same, and the
-- usual response to that is restarting something that was working.
--
-- So a stage says what it is on. A key and its arguments rather than a
-- sentence, because the sentence belongs in the reader's language and the
-- worker does not know which one that is.
--
-- Cleared when the stage stops. A finished stage that still says "exporting
-- CapTranslator" is worse than one that says nothing.
IF COL_LENGTH('ops.RunStage', 'Progress') IS NULL
BEGIN
    ALTER TABLE ops.RunStage ADD Progress NVARCHAR(400) NULL;
END
GO

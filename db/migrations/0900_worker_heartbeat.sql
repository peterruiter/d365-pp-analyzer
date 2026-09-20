-- ================================================= a worker that stopped mid run ==
-- The claim query reads "WHERE ClaimedUtc IS NULL". Once a command is claimed
-- it can never be picked up again, so a worker that stops between claiming a
-- command and completing it orphans that command for ever. The run stays
-- "running", the stage stays "running", nothing retries, and the screen shows
-- a run that is going and is not.
--
-- This is not a rare case. The worker is a container: every deployment replaces
-- it, and a deployment during an extraction is a Tuesday. The stuck-run health
-- check even describes the situation -- "a worker replaced mid-run leaves its
-- run marked running, the ledger is intact, so the work is recoverable" -- and
-- nothing anywhere recovered it.
--
-- A heartbeat rather than a timeout on the claim. A stage can legitimately run
-- for an hour: the checker is minutes at best and an extraction of a large
-- estate is longer, so "claimed more than N minutes ago" would steal work from
-- a worker that is doing it. The heartbeat is written by the poll loop rather
-- than by the stage, so a worker that is alive always has a recent one however
-- long the thing it is doing takes.
IF COL_LENGTH('ops.RunCommand', 'HeartbeatUtc') IS NULL
BEGIN
    ALTER TABLE ops.RunCommand ADD HeartbeatUtc DATETIME2(3) NULL;
END
GO

-- Claimed, unfinished, and not heard from. The claim query reads this as well
-- as the unclaimed ones, so an abandoned command is picked up by whichever
-- worker is running now.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RunCommand_Abandoned')
BEGIN
    CREATE INDEX IX_RunCommand_Abandoned ON ops.RunCommand (HeartbeatUtc)
        WHERE CompletedUtc IS NULL AND ClaimedUtc IS NOT NULL;
END
GO

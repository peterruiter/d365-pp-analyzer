-- ================================================ names a real estate carries ==
-- A plug-in's schema name is a fully qualified .NET type name, and Microsoft's
-- own run past two hundred characters without trying:
--
--   Microsoft.Dynamics.OmnichannelReferenceDataSyncPlugins.Plugins
--     .AgentAvailabilityStatusPlugins.PostOperationAgentAvailabilityStatusUpdate
--
-- SchemaName was NVARCHAR(200), so the extract stage failed with "String or
-- binary data would be truncated", named the column, and lost the whole batch
-- rather than the one row that was too long.
--
-- Found the first time a run read a production environment with the Omnichannel
-- solutions in it. The synthetic sample and the nine exports swept before it all
-- carry names a person typed, and a person does not type two hundred characters.
--
-- 400, matching DisplayName beside it. NVARCHAR is stored by length, so the
-- extra room costs nothing until it is used.
IF COL_LENGTH('inv.Component', 'SchemaName') IS NOT NULL
BEGIN
    ALTER TABLE inv.Component ALTER COLUMN SchemaName NVARCHAR(400) NULL;
END
GO

-- The platform's own identifier for the component. Usually a GUID, occasionally
-- a composite, and widened for the same reason: a truncation here corrupts the
-- key the whole inventory joins on rather than one label.
--
-- StableKey is deliberately left at 400. It sits in a unique constraint, so
-- widening it rebuilds an index for a column that has never overflowed, and the
-- risk of that is larger than the problem.
IF COL_LENGTH('inv.Component', 'PlatformId') IS NOT NULL
BEGIN
    ALTER TABLE inv.Component ALTER COLUMN PlatformId NVARCHAR(400) NULL;
END
GO

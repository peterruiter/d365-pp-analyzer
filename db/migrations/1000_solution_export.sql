-- ========================================== exporting what a live run reads ==
-- The fourth optional check.
--
-- Seventeen of this product's rules read a solution file: fourteen unpack the
-- zip and three need Microsoft's checker, which takes one. A run against a
-- live environment reported all seventeen as not assessed and an uploaded copy
-- of the same solutions reported them fine, which made the offline mode look
-- like the richer one.
--
-- It never was. A connection that can read an environment can ask it for the
-- same zip a person downloads, and extraction-sources.json has always declared
-- every live mode as reaching solutionZip and checker in full. The declaration
-- was right and nothing implemented it.
--
-- A switch rather than always-on, because it is slow: one small solution took
-- seventy seconds against a real environment, so a dozen is a quarter of an
-- hour. An assessment pays that; a quick scan, which promises an answer in
-- fifteen minutes, does not.
IF COL_LENGTH('ops.RunSelection', 'ExportSolutions') IS NULL
BEGIN
    ALTER TABLE ops.RunSelection ADD ExportSolutions BIT NULL;
END
GO

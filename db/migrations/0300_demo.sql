-- ============================================================== demo seed ==
-- The stamp that says which version of the demonstration estate this database
-- holds.
--
-- The seeder rebuilds the demonstration when this number differs from the one
-- compiled into the product, rather than when the engagement is missing. That
-- is what keeps it current: an engagement created only when absent stays frozen
-- at whatever it looked like the first time a container started, and never
-- gains what a later release added.
--
-- Declared here rather than created by the seeder, because every other table in
-- this database is declared in a migration and because the product's managed
-- identity is not meant to be creating tables at runtime.
IF OBJECT_ID('ops.DemoSeed') IS NULL
BEGIN
    CREATE TABLE ops.DemoSeed
    (
        EngagementId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_DemoSeed PRIMARY KEY,
        SeedVersion  INT              NOT NULL,
        AppliedUtc   DATETIME2(3)     NOT NULL CONSTRAINT DF_DemoSeed_AppliedUtc DEFAULT SYSUTCDATETIME()
        -- No foreign key to ops.Engagement. The seeder deletes the engagement and
        -- writes it again, and a cascade here would take the stamp with it, which
        -- would make every start look like a first start. The stamp is read
        -- through a join to the engagement instead, so a stamp whose engagement
        -- somebody deleted by hand simply rebuilds.
    );
END
GO

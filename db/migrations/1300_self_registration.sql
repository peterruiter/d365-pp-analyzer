-- =============================================== settings, and letting people in ==
-- A place for the handful of switches a global administrator owns.
--
-- Key and value rather than a column per setting, because the alternative is a
-- migration every time somebody wants a checkbox, and this table will never be
-- large enough for the shape to cost anything.
IF OBJECT_ID('ops.SystemSetting') IS NULL
BEGIN
    CREATE TABLE ops.SystemSetting
    (
        Name        NVARCHAR(100)  NOT NULL CONSTRAINT PK_SystemSetting PRIMARY KEY,
        Value       NVARCHAR(400)  NOT NULL,
        UpdatedUtc  DATETIME2(3)   NOT NULL CONSTRAINT DF_SystemSetting_UpdatedUtc DEFAULT SYSUTCDATETIME(),

        -- Who turned it on. A switch that decides who may enter the product is one
        -- somebody has to be answerable for having moved.
        UpdatedBy   NVARCHAR(200)  NOT NULL
    );
END
GO

-- ------------------------------------------------------- who may admit themselves --
-- access.selfRegistration decides whether signing in creates a user row for
-- somebody who does not have one.
--
-- Absent means on, which is the default asked for, and it is worth writing down
-- exactly what "on" grants: a row in ops.SystemUser with IsGlobalAdmin = 0 and no
-- engagement membership at all. Every engagement query filters on membership, so a
-- self-registered person signs in and sees an empty product. They cannot see the
-- demonstration estate, any client's findings, or that any engagement exists.
--
-- That distinction is the whole reason this is safe to default on, and it is the
-- thing to re-read before anybody changes what a bare user row can reach.
GO

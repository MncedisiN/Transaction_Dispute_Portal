IF OBJECT_ID(N'dbo.schema_migrations',
    N'U') IS NULL CREATE TABLE dbo.schema_migrations  (
    version varchar(100) NOT NULL PRIMARY KEY,
    applied_at datetimeoffset(7) NOT NULL DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')
)

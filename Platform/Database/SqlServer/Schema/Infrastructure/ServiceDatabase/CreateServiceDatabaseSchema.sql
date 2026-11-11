IF SCHEMA_ID(N'configuration') IS NULL
    EXEC(N'CREATE SCHEMA configuration');

IF OBJECT_ID(N'configuration.serviceSchemaVersions') IS NULL
    CREATE TABLE configuration."serviceSchemaVersions" (
        "serviceName" varchar(40) NOT NULL,
        version varchar(80) NOT NULL,
        checksum char(64) NOT NULL,
        "appliedAt" datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
        PRIMARY KEY ("serviceName", version)
    );

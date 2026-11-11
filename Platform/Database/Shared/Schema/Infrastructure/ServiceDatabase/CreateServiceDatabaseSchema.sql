CREATE SCHEMA IF NOT EXISTS configuration;

CREATE TABLE IF NOT EXISTS configuration."serviceSchemaVersions" (
    "serviceName" varchar(40) NOT NULL,
    version varchar(80) NOT NULL,
    checksum char(64) NOT NULL,
    "appliedAt" timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY ("serviceName", version)
);

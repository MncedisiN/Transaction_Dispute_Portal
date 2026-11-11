CREATE TABLE dbo.database_installation (
    component varchar(40) PRIMARY KEY,
    checksum varchar(64) NOT NULL,
    installed_at datetimeoffset NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
INSERT INTO dbo.database_installation (component, checksum)
VALUES ('Api', '1B192F91056A65431FB0578061636AF0916941CA471EC0334650C817A859E02E');
COMMIT TRANSACTION;

UPDATE {{MigrationHistoryTable}}
SET
    checksum = @Checksum
WHERE version = @Version
AND checksum IS NULL

INSERT INTO {{MigrationHistoryTable}}(
    version,
    checksum
)
VALUES (
    @Version,
    @Checksum
)

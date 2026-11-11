INSERT INTO ledger."bookImports"(
    "accountId",
    "sourceHash",
    "snapshotPublicId",
    "transactionCount",
    "openingBalance",
    "openingReserved"
)
VALUES (
    @InternalId,
    @Hash,
    @SnapshotId,
    @Count,
    @ExpectedBalance,
    @ExpectedReserved
)

INSERT "integration"."accountMigrations"(
    "accountId",
    "snapshotId",
    "sourceHash",
    "countryCode",
    state,
    "capturedAt",
    "activatedAt"
)
VALUES (
    @AccountKey,
    @SnapshotId,
    @Hash,
    'ZA',
    'Active',
    @CapturedAt,
    @CapturedAt
);

INSERT "integration"."localBankingIdentities"(
    "userId",
    "createdAt"
)
VALUES (
    @CustomerKey,
    @CapturedAt
);

INSERT "integration"."localBankingAccounts"(
    "accountId",
    "bankingIdentityId",
    "openingRequestId",
    "initialCount"
)
SELECT
    @AccountKey,
    id,
    @SnapshotId,
    6
FROM "integration"."localBankingIdentities"
WHERE "userId" = @CustomerKey;

INSERT "transactions"."duplicateConfirmations"(
    "transactionId",
    "originalTransactionId",
    "confirmationSource"
)
SELECT
    duplicate.id,
    original.id,
    'LocalBankFeed'
FROM "transactions"."transactions" duplicate
CROSS JOIN "transactions"."transactions" original
WHERE duplicate."publicId" = @Duplicate
AND original."publicId" = @Original;

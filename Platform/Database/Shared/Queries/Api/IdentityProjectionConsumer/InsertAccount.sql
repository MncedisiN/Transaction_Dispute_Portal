INSERT INTO "accounts"."accounts"(
    "publicId",
    "userId",
    "accountType",
    "currencyCode",
    "maskedAccountNumber",
    "protectedNumber",
    "accountStatus",
    "countryPolicyVersion",
    "createdAt"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "identity"."users" AS "resolved1"
        WHERE "resolved1"."publicId" = @UserId),
    @Type,
    @Currency,
    @Masked,
    @Protected,
    @Status,
    @PolicyVersion,
    @CreatedAt
);

INSERT INTO "integration"."accountMigrations"(
    "accountId",
    "snapshotId",
    "sourceHash",
    "countryCode",
    "state",
    "capturedAt",
    "activatedAt"
)
VALUES (
    (
        SELECT
            "resolved2"."id"
        FROM "accounts"."accounts" AS "resolved2"
        WHERE "resolved2"."publicId" = @Id),
    @Id,
    @Hash,
    @Country,
    'Active',
    @Now,
    @Now
)

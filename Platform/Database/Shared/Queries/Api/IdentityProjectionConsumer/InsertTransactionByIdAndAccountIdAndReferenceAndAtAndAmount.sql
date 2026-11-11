INSERT INTO "transactions"."transactions"(
    "publicId",
    "accountId",
    "transactionReference",
    "transactionAt",
    "description",
    "merchantName",
    "amount",
    "currencyCode",
    "transactionTypeId",
    "direction",
    "transactionStatus",
    "disputeEligible"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "accounts"."accounts" AS "resolved1"
        WHERE "resolved1"."publicId" = @AccountId),
    @Reference,
    @At,
    'Incoming transfer',
    'Dispute Portal',
    @Amount,
    @Currency,
    (
        SELECT
            "resolved2"."id"
        FROM "configuration"."transactionTypes" AS "resolved2"
        WHERE "resolved2"."code" = 'Transfer'),
    'Credit',
    'Posted',
    @Eligible
)

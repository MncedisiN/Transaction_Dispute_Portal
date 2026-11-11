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
    "disputeEligible",
    "debitOrderScheme",
    "mandateStatus"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "accounts"."accounts" AS "resolved1"
        WHERE "resolved1"."publicId" = @Account),
    @Reference,
    @At,
    @Description,
    @MerchantName,
    @Amount,
    @Currency,
    (
        SELECT
            "resolved2"."id"
        FROM "configuration"."transactionTypes" AS "resolved2"
        WHERE "resolved2"."code" = @Type),
    @Direction,
    @Status,
    @Eligible,
    @DebitOrderScheme,
    @MandateStatus
)

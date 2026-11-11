INSERT INTO "transactions"."bankLinks"(
    "accountId",
    "externalTransactionHash",
    "transactionId"
)
VALUES (
    (
        SELECT
            "resolved1"."id"
        FROM "accounts"."accounts" AS "resolved1"
        WHERE "resolved1"."publicId" = @Account),
    @Hash,
    (
        SELECT
            "resolved2"."id"
        FROM "transactions"."transactions" AS "resolved2"
        WHERE "resolved2"."publicId" = @Id)
)

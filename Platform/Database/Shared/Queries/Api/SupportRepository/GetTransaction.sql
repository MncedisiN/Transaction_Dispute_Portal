SELECT
    (
    SELECT
        "related1"."code"
    FROM "configuration"."transactionTypes" AS "related1"
    WHERE "related1"."id" = "transactions"."transactionTypeId") AS "transaction_type"
FROM "transactions"."transactions" AS "transactions"
WHERE "transactions"."publicId" = @Id

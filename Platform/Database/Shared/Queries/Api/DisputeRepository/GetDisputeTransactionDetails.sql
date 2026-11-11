SELECT
    "transactions"."transactionReference" AS TransactionReference,
    "transactions"."merchantName" AS MerchantName,
    "transactions"."transactionAt" AS TransactionAt,
    (
    SELECT
        "related1"."code"
    FROM "configuration"."transactionTypes" AS "related1"
    WHERE "related1"."id" = "transactions"."transactionTypeId") AS TransactionType
FROM "transactions"."transactions" AS "transactions"
WHERE "transactions"."publicId" = @Id

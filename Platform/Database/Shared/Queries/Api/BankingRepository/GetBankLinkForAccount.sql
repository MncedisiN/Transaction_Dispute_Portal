SELECT
    "t"."publicId" AS "id",
    "t"."amount" AS "amount",
    "t"."currencyCode" AS "currency",
    "t"."transactionAt" AS TransactionAt,
    "t"."description" AS "description",
    "t"."merchantName" AS MerchantName,
    (
    SELECT
        "related1"."code"
    FROM "configuration"."transactionTypes" AS "related1"
    WHERE "related1"."id" = "t"."transactionTypeId") AS Type,
    "t"."direction" AS "direction"
FROM "transactions"."bankLinks" l
JOIN "transactions"."transactions" t ON "t"."id" = "l"."transactionId"
WHERE (
    SELECT
        "related2"."publicId"
    FROM "accounts"."accounts" AS "related2"
    WHERE "related2"."id" = "l"."accountId") = @Account
AND "l"."externalTransactionHash" = @Hash

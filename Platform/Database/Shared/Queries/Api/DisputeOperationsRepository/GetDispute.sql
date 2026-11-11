SELECT
    (
    SELECT
        "related1"."code"
    FROM "configuration"."transactionTypes" AS "related1"
    WHERE "related1"."id" = "t"."transactionTypeId") AS TransactionType,
    "t"."amount" AS "amount",
    "t"."currencyCode" AS "currency",
    (
    SELECT
        "related2"."publicId"
    FROM "identity"."users" AS "related2"
    WHERE "related2"."id" = "d"."customerId") AS CustomerId,
    "d"."createdAt" AS CreatedAt
FROM "disputes"."disputes" d
JOIN "transactions"."transactions" t ON "t"."id" = "d"."transactionId"
WHERE "d"."publicId" = @Id

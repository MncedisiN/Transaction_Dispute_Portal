SELECT
    "d"."publicId" AS "id",
    "d"."reference" AS "reference",
    (
    SELECT
        "related1"."publicId"
    FROM "transactions"."transactions" AS "related1"
    WHERE "related1"."id" = "d"."transactionId") AS TransactionId,
    "t"."description" AS TransactionDescription,
    "t"."amount" AS "amount",
    "t"."currencyCode" AS "currency",
    (
    SELECT
        "related2"."code"
    FROM "configuration"."disputeCategories" AS "related2"
    WHERE "related2"."id" = "d"."categoryId") AS "category",
    "d"."status" AS "status",
    CAST("d"."priority" AS int) AS Priority,
    "d"."version" AS "version",
    "d"."createdAt" AS CreatedAt,
    "d"."resolution" AS "resolution",
    "p"."displayName" AS CustomerName,
    "p"."maskedEmail" AS MaskedEmail,
    "a"."maskedAccountNumber" AS AccountNumber,
    "t"."transactionReference" AS TransactionReference,
    "t"."merchantName" AS MerchantName,
    "t"."transactionAt" AS TransactionAt,
    (
    SELECT
        "related3"."code"
    FROM "configuration"."transactionTypes" AS "related3"
    WHERE "related3"."id" = "t"."transactionTypeId") AS TransactionType

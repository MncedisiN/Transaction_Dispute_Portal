SELECT
    "o"."publicId" AS "id",
    (
    SELECT
        "related1"."publicId"
    FROM "accounts"."accounts" AS "related1"
    WHERE "related1"."id" = "o"."accountId") AS Account,
    "o"."amount" AS "amount",
    "o"."currencyCode" AS "currency",
    "o"."merchantName" AS Merchant,
    (
    SELECT
        "related2"."code"
    FROM "configuration"."transactionTypes" AS "related2"
    WHERE "related2"."id" = "o"."transactionTypeId") AS Type,
    "o"."direction" AS "direction",
    "o"."transactionStatus" AS Status,
    "o"."transactionAt" AS OriginalAt,
    "p"."confirmationSource" AS Proof,
    "t"."merchantName" AS DuplicateMerchant,
    (
    SELECT
        COUNT(*)
    FROM "disputes"."disputes" d
    WHERE "d"."transactionId" = "o"."id") AS Disputed
FROM "transactions"."duplicateConfirmations" p
JOIN "transactions"."transactions" o ON "o"."id" = "p"."originalTransactionId"
JOIN "transactions"."transactions" t ON "t"."id" = "p"."transactionId"
WHERE (
    SELECT
        "related3"."publicId"
    FROM "transactions"."transactions" AS "related3"
    WHERE "related3"."id" = "p"."transactionId") = @Id
AND "o"."archivedAt" IS NULL

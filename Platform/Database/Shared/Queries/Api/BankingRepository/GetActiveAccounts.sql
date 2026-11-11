SELECT
    "customer_accounts"."publicId" AS "id",
    "customer_accounts"."accountType" AS AccountType,
    "customer_accounts"."currencyCode" AS "currency",
    "customer_accounts"."maskedAccountNumber" AS MaskedAccountNumber,
    "customer_accounts"."accountStatus" AS Status,
    "customer_accounts"."createdAt" AS RegisteredAt
FROM "accounts"."accounts" AS "customer_accounts"
WHERE "customer_accounts"."publicId" = @AccountPublicId
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "customer_accounts"."userId") = @CustomerPublicId
AND "customer_accounts"."accountStatus" <> 'Closed'
AND("customer_accounts"."accountType" IN ('Cheque', 'Savings')
    OR EXISTS (
        SELECT
            1
        FROM "integration"."accountMigrations" m
        WHERE "m"."accountId" = "customer_accounts"."id"
        AND "m"."state" = 'Active'))

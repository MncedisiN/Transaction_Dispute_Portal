SELECT
    "customer_accounts"."accountType" AS Name,
    "customer_accounts"."maskedAccountNumber" AS Number
FROM "accounts"."accounts" AS "customer_accounts"
WHERE "customer_accounts"."publicId" = @Id
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "customer_accounts"."userId") = @User

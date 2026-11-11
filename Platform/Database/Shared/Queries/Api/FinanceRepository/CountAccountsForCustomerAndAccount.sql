SELECT
    COUNT(*)
FROM "accounts"."accounts" AS "customer_accounts"
WHERE "customer_accounts"."publicId" = @Account
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "customer_accounts"."userId") = @Customer

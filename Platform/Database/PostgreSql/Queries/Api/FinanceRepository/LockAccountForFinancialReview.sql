SELECT
    "customer_accounts"."id" AS "row_id"
FROM "accounts"."accounts" AS "customer_accounts"
WHERE "customer_accounts"."publicId" = @Account FOR
UPDATE

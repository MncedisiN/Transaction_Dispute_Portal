SELECT
    "customer_accounts"."id" AS "row_id"
FROM "accounts"."accounts" AS "customer_accounts"
WITH (UPDLOCK, HOLDLOCK)
WHERE "customer_accounts"."publicId" = @Account

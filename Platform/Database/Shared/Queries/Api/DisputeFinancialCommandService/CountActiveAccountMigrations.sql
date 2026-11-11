SELECT
    COUNT(*)
FROM "integration"."accountMigrations" AS "platform_account_migrations"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "accounts"."accounts" AS "related1"
    WHERE "related1"."id" = "platform_account_migrations"."accountId") = @Id
AND "platform_account_migrations"."state" = 'Active'

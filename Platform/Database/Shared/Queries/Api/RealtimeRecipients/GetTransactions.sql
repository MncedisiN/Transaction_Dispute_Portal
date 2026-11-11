SELECT
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "a"."userId") AS "user_id"
FROM "transactions"."transactions" t
JOIN "accounts"."accounts" a ON "a"."id" = "t"."accountId"
WHERE "t"."publicId" = @Id

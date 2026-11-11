UPDATE "accounts"."accounts"
SET
    "protectedNumber" = @Protected,
    "numberHash" = @Hash
WHERE "accounts"."publicId" = @Account
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "accounts"."userId") = @Owner
AND "accounts"."protectedNumber" IS NULL

SELECT
    (
    SELECT
        "related1"."publicId"
    FROM "accounts"."accounts" AS "related1"
    WHERE "related1"."id" = "l"."accountId") AS AccountId,
    "l"."provider" AS "provider",
    "l"."protectedExternalAccount" AS ProtectedAccount,
    "l"."protectedCursor" AS ProtectedCursor,
    "l"."revision" AS "revision",
    "l"."lastSyncedAt" AS LastSyncedAt
FROM "accounts"."bankLinks" l
JOIN "accounts"."accounts" a ON "a"."id" = "l"."accountId"
WHERE (
    SELECT
        "related2"."publicId"
    FROM "accounts"."accounts" AS "related2"
    WHERE "related2"."id" = "l"."accountId") = @Account
AND(
    SELECT
        "related3"."publicId"
    FROM "identity"."users" AS "related3"
    WHERE "related3"."id" = "a"."userId") = @Customer

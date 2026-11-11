UPDATE "accounts"."bankLinks"
SET
    "protectedCursor" = @Cursor,
    "revision" = "bankLinks"."revision" + 1,
    "lastSyncedAt" = @Now
WHERE (
    SELECT
        "related1"."publicId"
    FROM "accounts"."accounts" AS "related1"
    WHERE "related1"."id" = "bankLinks"."accountId") = @Account

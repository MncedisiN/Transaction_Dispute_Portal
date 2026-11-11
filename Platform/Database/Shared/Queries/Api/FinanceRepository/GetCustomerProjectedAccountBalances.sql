SELECT
    "a"."publicId" AS AccountId,
    "a"."currencyCode" AS "currency",
    "a"."accountStatus" AS Status,
    "a"."accountType" AS AccountType,
    COALESCE(SUM("l"."currentDelta"), 0) AS CurrentAmount,
    COALESCE(SUM("l"."reservedDelta"), 0) AS ReservedAmount,
    COALESCE(SUM(CAST("l"."unknownDelta" AS int)), 0) + CASE WHEN EXISTS (
    SELECT
        1
    FROM "accounts"."bankLinks" b
    WHERE "b"."accountId" = "a"."id") THEN 1 ELSE 0 END AS Unknown
FROM "accounts"."accounts" a
LEFT JOIN "ledger"."projectionEntries" l ON "l"."accountId" = "a"."id"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "a"."userId") = @Customer
GROUP BY "a"."id", "a"."publicId", "a"."currencyCode", "a"."accountStatus", "a"."accountType"

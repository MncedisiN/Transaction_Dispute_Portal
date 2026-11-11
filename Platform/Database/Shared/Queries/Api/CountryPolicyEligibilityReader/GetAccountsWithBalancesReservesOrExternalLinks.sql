SELECT
    account."publicId" AS "AccountId",
    COALESCE(SUM(entry."currentDelta"), 0) AS "CurrentAmount",
    COALESCE(SUM(entry."reservedDelta"), 0) AS "ReservedAmount",
    COALESCE(SUM(CAST(entry."unknownDelta" AS int)), 0) + CASE WHEN EXISTS (
    SELECT
        1
    FROM "accounts"."bankLinks" AS link
    WHERE link."accountId" = account."id") THEN 1 ELSE 0 END AS "Unknown"
FROM "accounts"."accounts" AS account
LEFT JOIN "ledger"."projectionEntries" AS entry ON entry."accountId" = account."id"
GROUP BY account."id", account."publicId"
HAVING COALESCE(SUM(entry."currentDelta"), 0) <> 0
OR COALESCE(SUM(entry."reservedDelta"), 0) <> 0
OR COALESCE(SUM(CAST(entry."unknownDelta" AS int)), 0) <> 0
OR EXISTS (
    SELECT
        1
    FROM "accounts"."bankLinks" AS link
    WHERE link."accountId" = account."id")

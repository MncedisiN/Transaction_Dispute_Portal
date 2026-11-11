SELECT
    COALESCE(SUM("financial_ledger"."currentDelta"), 0) AS CurrentAmount,
    COALESCE(SUM("financial_ledger"."reservedDelta"), 0) AS ReservedAmount,
    COALESCE(SUM(CAST("financial_ledger"."unknownDelta" AS int)), 0) AS Unknown
FROM "ledger"."projectionEntries" AS "financial_ledger"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "accounts"."accounts" AS "related1"
    WHERE "related1"."id" = "financial_ledger"."accountId") = @Account

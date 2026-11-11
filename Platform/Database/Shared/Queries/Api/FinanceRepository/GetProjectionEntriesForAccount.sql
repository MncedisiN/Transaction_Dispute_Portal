SELECT
    "l"."sequenceNo" AS EntryId,
    (
    SELECT
        "related1"."publicId"
    FROM "transactions"."transactions" AS "related1"
    WHERE "related1"."id" = "l"."transactionId") AS TransactionId,
    "l"."currentDelta" AS CurrentDelta,
    "l"."reservedDelta" AS ReservedDelta,
    "l"."recordedAt" AS RecordedAt,
    "t"."description" AS "description"
FROM "ledger"."projectionEntries" l
JOIN "transactions"."transactions" t ON "t"."id" = "l"."transactionId"
WHERE (
    SELECT
        "related2"."publicId"
    FROM "accounts"."accounts" AS "related2"
    WHERE "related2"."id" = "l"."accountId") = @Account
ORDER BY "l"."sequenceNo" DESC
OFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY

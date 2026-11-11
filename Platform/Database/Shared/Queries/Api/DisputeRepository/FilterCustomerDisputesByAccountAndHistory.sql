FROM "disputes"."disputes" d
JOIN "transactions"."transactions" t ON "t"."id" = "d"."transactionId"
WHERE (@From IS NULL
    OR "d"."createdAt" >= @From)
AND(@To IS NULL
    OR "d"."createdAt" < @To)
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "d"."customerId") = @CustomerId
AND(@AccountId IS NULL
    OR(
        SELECT
            "related2"."publicId"
        FROM "accounts"."accounts" AS "related2"
        WHERE "related2"."id" = "t"."accountId") = @AccountId)
AND(@Status IS NULL
    OR "d"."status" = @Status
    OR(@Status = 'active'
        AND "d"."status" IN ('Submitted', 'UnderReview', 'AwaitingCustomer'))
    OR(@Status = 'closed'
        AND "d"."status" IN ('Resolved', 'Declined', 'Withdrawn')))
AND (
    "d"."status" IN ('Submitted', 'UnderReview', 'AwaitingCustomer')
    OR COALESCE("d"."resolvedAt", "d"."updatedAt") >= @ClosedSince
)

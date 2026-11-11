SELECT
    COALESCE(SUM("dispute_refunds"."amount"), 0)
FROM "disputes"."refunds" AS "dispute_refunds"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "dispute_refunds"."customerId") = @Customer
AND "dispute_refunds"."approvalSource" = 'System'
AND "dispute_refunds"."postedAt" >= @Day
AND "dispute_refunds"."postedAt" < @End
AND "dispute_refunds"."currencyCode" = @Currency

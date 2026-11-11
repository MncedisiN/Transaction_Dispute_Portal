WITH candidate AS (
    SELECT
        "publicId"
    FROM debt."reversalReviews"
    WHERE status = 'Pending'
    AND "nextAttemptAt" <= now()
    AND("leaseUntil" IS NULL
        OR "leaseUntil" < now())
    ORDER BY "createdAt" FOR
    UPDATE SKIP LOCKED
    LIMIT 1)
UPDATE debt."reversalReviews" r
SET
    "leaseId" = @Lease,
    "leaseUntil" = now() + interval '60 seconds'
FROM candidate
WHERE r."publicId" = candidate."publicId"
RETURNING r."publicId" AS "Id", r."requestBody"::text AS Body, r."leaseId" AS LeaseId

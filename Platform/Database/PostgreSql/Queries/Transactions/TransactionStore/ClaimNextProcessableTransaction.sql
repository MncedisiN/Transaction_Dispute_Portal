WITH candidate AS (
    SELECT
        "publicId"
    FROM transactions."transactions"
    WHERE status IN ('Initiated', 'Validated', 'Processing', 'Aborting')
    AND "nextAttemptAt" <= now()
    AND("leaseUntil" IS NULL
        OR "leaseUntil" < now())
    ORDER BY "createdAt" FOR
    UPDATE SKIP LOCKED
    LIMIT 1)
UPDATE transactions."transactions" t
SET
    "leaseId" = @Lease,
    "leaseUntil" = now() + interval '60 seconds'
FROM candidate
WHERE t."publicId" = candidate."publicId"
RETURNING t."publicId" AS "Id"

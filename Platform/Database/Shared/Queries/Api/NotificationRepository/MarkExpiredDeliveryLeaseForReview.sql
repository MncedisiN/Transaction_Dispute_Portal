UPDATE "notifications"."deliveries"
SET
    "state" = 'Review',
    "lastError" = 'delivery_outcome_unknown',
    "completedAt" = @Now,
    "leaseUntil" = NULL
WHERE "deliveries"."state" = 'Processing'
AND "deliveries"."leaseUntil" < @Now

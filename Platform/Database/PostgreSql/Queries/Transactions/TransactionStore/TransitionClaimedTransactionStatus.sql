UPDATE transactions."transactions"
SET
    status = @Status,
    "failureCode" = @Code,
    "leaseId" = NULL,
    "leaseUntil" = NULL,
    attempts = 0,
    "nextAttemptAt" = now(),
    "updatedAt" = now()
WHERE "publicId" = @Id
AND "leaseId" = @LeaseId
AND status = @Old

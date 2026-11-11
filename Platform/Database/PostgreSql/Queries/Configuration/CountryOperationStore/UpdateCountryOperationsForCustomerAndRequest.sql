UPDATE configuration."countryOperations"
SET
    "settledAt" = COALESCE("settledAt", now())
WHERE "sourceKind" = @SourceKind
AND "requestId" = @RequestId
AND "sourceService" = @Caller
AND "customerPublicId" IS NOT DISTINCT
FROM @CustomerId

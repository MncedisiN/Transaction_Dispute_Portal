SELECT
    "sourceService" = @Caller
AND "countryCode" = @CountryCode
AND currency = @Currency
AND "originalTransactionId" IS NOT DISTINCT
FROM @OriginalTransactionId
AND "customerPublicId" IS NOT DISTINCT
FROM @CustomerId
FROM configuration."countryOperations"
WHERE "sourceKind" = @SourceKind
AND "requestId" = @RequestId

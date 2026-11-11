SELECT
    "requestId" AS RequestId,
    "operationId" AS OperationId,
    "sourceKind" AS SourceKind,
    "policyVersion" AS PolicyVersion,
    "countryCode" AS CountryCode,
    currency,
    "originalTransactionId" AS OriginalTransactionId,
    "customerPublicId" AS CustomerId,
    "settledAt" AS SettledAt
FROM configuration."countryOperations"
WHERE "operationId" = @OperationId
ORDER BY "settledAt" NULLS FIRST, id DESC
LIMIT 1

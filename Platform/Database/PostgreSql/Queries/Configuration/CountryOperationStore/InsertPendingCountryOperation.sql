INSERT INTO configuration."countryOperations"(
    "requestId",
    "operationId",
    "sourceKind",
    "sourceService",
    "policyVersion",
    "countryCode",
    currency,
    "originalTransactionId",
    "customerPublicId"
)
VALUES (
    @RequestId,
    @OperationId,
    @SourceKind,
    @Caller,
    @PolicyVersion,
    @CountryCode,
    @Currency,
    @OriginalTransactionId,
    @CustomerId
)

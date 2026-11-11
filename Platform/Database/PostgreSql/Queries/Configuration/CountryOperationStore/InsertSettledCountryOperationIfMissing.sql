INSERT INTO configuration."countryOperations"(
    "requestId",
    "operationId",
    "sourceKind",
    "sourceService",
    "policyVersion",
    "countryCode",
    currency,
    "customerPublicId",
    "settledAt"
)
VALUES (
    @RequestId,
    @OperationId,
    @SourceKind,
    @Caller,
    @PolicyVersion,
    @Country,
    @Currency,
    @CustomerId,
    now()
) ON CONFLICT("sourceKind", "requestId") DO NOTHING

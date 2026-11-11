EXEC sp_set_session_context @key = N'canonical_projection', @value = 1;

INSERT "transactions"."transactions"(
    "publicId",
    "accountId",
    "transactionReference",
    "transactionAt",
    description,
    "merchantName",
    amount,
    "currencyCode",
    "transactionTypeId",
    direction,
    "transactionStatus",
    "disputeEligible"
)
VALUES (
    @Id,
    @AccountKey,
    'PRECISION-CHECK',
    @Now,
    'Precision verification',
    'Verification',
    0.12345678,
    'ZAR',
    @TypeKey,
    'Debit',
    'Reversed',
    0
);

EXEC sp_set_session_context @key = N'canonical_projection', @value = NULL;

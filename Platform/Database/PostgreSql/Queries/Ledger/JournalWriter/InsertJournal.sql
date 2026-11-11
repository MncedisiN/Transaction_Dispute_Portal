INSERT INTO ledger."journals"(
    "publicId",
    "transactionPublicId",
    reference,
    kind,
    "requestHash",
    "requestBody",
    status,
    "createdAt"
)
VALUES (
    @Id,
    @TransactionId,
    @Reference,
    @Kind,
    @Hash,
    CAST(@Body AS jsonb),
    'Draft',
    @Created
)
RETURNING id

INSERT INTO transactions."receivedTransactions"(
    "publicId",
    "transactionId",
    "accountPublicId",
    "customerPublicId",
    body,
    "occurredAt"
)
VALUES (
    @Id,
    (
        SELECT
            id
        FROM transactions.transactions
        WHERE "publicId" = @Transaction),
    @AccountId,
    @UserId,
    CAST(@Body AS jsonb),
    @At
) ON CONFLICT DO NOTHING

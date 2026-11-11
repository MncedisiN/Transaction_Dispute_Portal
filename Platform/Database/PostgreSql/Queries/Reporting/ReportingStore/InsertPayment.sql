INSERT INTO reporting."payments"(
    "publicId",
    currency,
    amount,
    state,
    "completedAt"
)
VALUES (
    @PaymentId,
    @Currency,
    @Amount,
    @State,
    @At
) ON CONFLICT DO NOTHING

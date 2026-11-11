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
    @Reference,
    @OccurredAt,
    @Description,
    @MerchantName,
    @Amount,
    @Currency,
    @TypeKey,
    @Direction,
    @Status,
    @DisputeEligible
)

INSERT INTO "disputes"."refundRecalls"(
    "publicId",
    "disputeId",
    "creditTransactionId",
    "reversalTransactionId",
    "recalledById",
    "requestId",
    "expectedVersion",
    "amount",
    "currencyCode",
    "reason",
    "postedAt"
)
SELECT
    @Recall,
    "d"."id",
    "credit"."id",
    "reversal"."id",
    "u"."id",
    @RequestId,
    @ExpectedVersion,
    @Amount,
    @Currency,
    @Reason,
    @Now
FROM "disputes"."disputes" d
JOIN "transactions"."transactions" credit ON "credit"."publicId" = @Credit
JOIN "transactions"."transactions" reversal ON "reversal"."publicId" = @Reversal
JOIN "identity"."users" u ON "u"."publicId" = @Actor
WHERE "d"."publicId" = @Dispute

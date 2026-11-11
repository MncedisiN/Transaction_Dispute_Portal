SELECT
    "r"."publicId" AS "id",
    "d"."publicId" AS DisputeId,
    "credit"."publicId" AS CreditTransactionId,
    "reversal"."publicId" AS ReversalTransactionId,
    "r"."amount" AS "amount",
    "r"."currencyCode" AS "currency",
    "r"."postedAt" AS PostedAt,
    "r"."reason" AS "reason",
    "u"."publicId" AS Actor,
    "r"."requestId" AS RequestId,
    "r"."expectedVersion" AS ExpectedVersion
FROM "disputes"."refundRecalls" r
JOIN "disputes"."disputes" d ON "d"."id" = "r"."disputeId"
JOIN "transactions"."transactions" credit ON "credit"."id" = "r"."creditTransactionId"
JOIN "transactions"."transactions" reversal ON "reversal"."id" = "r"."reversalTransactionId"
JOIN "identity"."users" u ON "u"."id" = "r"."recalledById"
WHERE "d"."publicId" = @Id

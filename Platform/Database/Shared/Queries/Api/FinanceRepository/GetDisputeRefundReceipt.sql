SELECT
    dispute."publicId" AS DisputeId,
    credit."publicId" AS CreditTransactionId,
    refund.amount AS Amount,
    refund."currencyCode" AS Currency,
    refund."postedAt" AS PostedAt,
    refund."approvalSource" AS Source,
    approver."publicId" AS Actor,
    refund."requestId" AS RequestId,
    refund.reason AS Reason,
    credit."transactionReference" AS CreditReference,
    receipt."customerReference" AS CustomerReference
FROM "disputes"."refunds" refund
JOIN "disputes"."disputes" dispute ON dispute.id = refund."disputeId"
JOIN "transactions"."transactions" credit ON credit.id = refund."creditTransactionId"
LEFT JOIN "identity"."users" approver ON approver.id = refund."approvedById"
LEFT JOIN "disputes"."refundReceipts" receipt ON receipt."refundId" = refund.id
WHERE dispute."publicId" = @Id

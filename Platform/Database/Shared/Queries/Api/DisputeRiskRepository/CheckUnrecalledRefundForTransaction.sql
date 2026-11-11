SELECT
    1
FROM "disputes"."refunds" refund
WHERE refund."transactionId" = t."id"
AND NOT EXISTS (
    SELECT
        1
    FROM "disputes"."refundRecalls" recall
    WHERE recall."disputeId" = refund."disputeId")

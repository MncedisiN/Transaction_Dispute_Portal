SELECT
    COUNT(*)
FROM "disputes"."refunds" r
WHERE (
    SELECT
        "related1"."publicId"
    FROM "transactions"."transactions" AS "related1"
    WHERE "related1"."id" = "r"."transactionId") = @Id
AND NOT EXISTS (
    SELECT
        1
    FROM "disputes"."refundRecalls" recall
    WHERE "recall"."disputeId" = "r"."disputeId")

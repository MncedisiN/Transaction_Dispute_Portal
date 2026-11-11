SELECT
    COUNT(*)
FROM "disputes"."disputes" parent
JOIN "transactions"."transactions" t ON "t"."id" = "parent"."transactionId"
JOIN "accounts"."accounts" a ON "a"."id" = "t"."accountId"
WHERE "parent"."publicId" = @Id
AND("a"."accountStatus" <> 'Active'
    OR EXISTS (
        SELECT
            1
        FROM "disputes"."disputes" active
        WHERE "active"."transactionId" = "parent"."transactionId"
        AND "active"."status" IN ('Submitted', 'UnderReview', 'AwaitingCustomer'))
    OR EXISTS (
        SELECT
            1
        FROM "disputes"."financialCommands" financial_command
        JOIN "disputes"."disputes" previous_case ON previous_case."id" = financial_command."disputeId"
        WHERE previous_case."transactionId" = "parent"."transactionId"
        AND financial_command."state" NOT IN ('Posted', 'Failed'))
    OR EXISTS (
        SELECT
            1
        FROM "disputes"."refunds" refund
        WHERE "refund"."transactionId" = "parent"."transactionId"
        AND NOT EXISTS (
            SELECT
                1
            FROM "disputes"."refundRecalls" recall
            WHERE "recall"."disputeId" = "refund"."disputeId")))

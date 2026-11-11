SELECT
    current_case."publicId"
FROM disputes.disputes current_case
WHERE {{MembershipPredicate}}
AND(EXISTS (
        SELECT
            1
        FROM disputes.refunds refund
        WHERE refund."disputeId" = current_case.id)
    OR EXISTS (
        SELECT
            1
        FROM disputes.disputes active_case
        WHERE active_case."transactionId" = current_case."transactionId"
        AND active_case.id <> current_case.id
        AND active_case.status IN ('Submitted', 'UnderReview', 'AwaitingCustomer')))

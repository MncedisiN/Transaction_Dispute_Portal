SELECT
    t."amount" AS Amount,
    t."currencyCode" AS Currency,
    tt."code" AS TransactionType,
    t."transactionAt" AS TransactionAt,
    t."debitOrderScheme" AS Scheme,
    t."mandateStatus" AS Mandate,
    {{EligibleTransactionExpression}} AS Eligible,
    (
    SELECT
        d."publicId"
    FROM "disputes"."disputes" d
    WHERE d."transactionId" = t."id"
    AND d."status" IN ('Submitted', 'UnderReview', 'AwaitingCustomer')) AS ActiveDisputeId,
    {{ExistsAsBoolean}} AS HasPendingFinancialOperation,
    {{RelatedExistsAsBoolean}} AS HasPostedRefund
FROM "transactions"."transactions" t
JOIN "accounts"."accounts" a ON a."id" = t."accountId"
JOIN "identity"."users" owner_user ON owner_user."id" = a."userId"
JOIN "configuration"."transactionTypes" tt ON tt."id" = t."transactionTypeId"
WHERE t."publicId" = @Transaction
AND t."archivedAt" IS NULL
AND owner_user."publicId" = @User
AND owner_user."accountStatus" = 'Active'

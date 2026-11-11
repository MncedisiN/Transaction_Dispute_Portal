SELECT
    d."publicId" AS DisputeId,
    r.amount AS Amount,
    r."currencyCode" AS Currency,
    a."accountType" AS AccountType,
    a."maskedAccountNumber" AS AccountNumber,
    d.reference AS Reference
FROM disputes."refundRecalls" r
JOIN disputes.disputes d ON d.id = r."disputeId"
JOIN transactions.transactions credit ON credit.id = r."creditTransactionId"
JOIN accounts.accounts a ON a.id = credit."accountId"
WHERE {{MembershipPredicate}}

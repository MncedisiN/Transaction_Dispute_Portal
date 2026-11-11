SELECT
    d."publicId" AS Id,
    u."publicId" AS Customer,
    t."publicId" AS TransactionId,
    a."publicId" AS Account,
    d.version AS Version,
    d.status AS Status,
    d.reference AS Reference,
    category.code AS Category,
    t.amount AS Amount,
    t."currencyCode" AS Currency,
    a."currencyCode" AS AccountCurrency,
    a."accountStatus" AS AccountStatus,
    CASE WHEN a."userId" = d."customerId"
AND u."accountStatus" = 'Active' THEN 1 ELSE 0 END AS CustomerActiveFlag,
    t.direction AS Direction,
    t."transactionStatus" AS PaymentStatus,
    transaction_type.code AS Type,
    t."transactionAt" AS TransactionAt,
    t."debitOrderScheme" AS Scheme,
    t."mandateStatus" AS Mandate,
    (
    SELECT
        COUNT(*)
    FROM accounts."bankLinks" bank_link
    WHERE bank_link."accountId" = a.id) AS ExternalCount,
    (
    SELECT
        COUNT(*)
    FROM integration."localBankingAccounts" bank_account
    WHERE bank_account."accountId" = a.id) AS LocalCount,
    CASE WHEN a."userId" = d."customerId"
AND a."accountStatus" = 'Active'
AND u."accountStatus" = 'Active' THEN 1 ELSE 0 END AS ActiveFlag,
    CASE WHEN p."emailVerified" = {{TrueValue}}
OR p."phoneVerified" = {{RelatedTrueValue}} THEN 1 ELSE 0 END AS VerifiedFlag
FROM disputes.disputes d
JOIN transactions.transactions t ON t.id = d."transactionId"
JOIN accounts.accounts a ON a.id = t."accountId"
JOIN "identity".users u ON u.id = d."customerId"
JOIN "identity"."customerProfiles" p ON p."userId" = u.id
JOIN configuration."disputeCategories" category ON category.id = d."categoryId"
JOIN configuration."transactionTypes" transaction_type ON transaction_type.id = t."transactionTypeId"
WHERE d."publicId" = @Id

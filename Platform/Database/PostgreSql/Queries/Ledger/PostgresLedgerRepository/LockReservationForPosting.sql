SELECT
    "publicId" AS TransactionId,
    "accountId" AS AccountId,
    "customerPublicId" AS UserId,
    currency,
    amount,
    "usageDay" AS UsageDay,
    state,
    "expiresAt" AS ExpiresAt,
    reference
FROM ledger."reservations"
WHERE "publicId" = @Id FOR
UPDATE

SELECT
    "publicId" AS "Id",
    "customerPublicId" AS UserId,
    "accountNumber" AS Number,
    "accountType" AS Type,
    "countryCode" AS CountryCode,
    currency,
    status,
    "ledgerBalance" AS LedgerBalance,
    "reservedBalance" AS ReservedBalance,
    "balanceVersion" AS Version,
    "createdAt" AS CreatedAt,
    "countryPolicyVersion" AS CountryPolicyVersion
FROM accounts."accounts"
WHERE "customerPublicId" = @User
ORDER BY "createdAt", "publicId"

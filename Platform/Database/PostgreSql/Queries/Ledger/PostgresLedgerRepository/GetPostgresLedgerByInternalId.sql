SELECT
    "publicId" AS AccountId,
    "customerPublicId" AS UserId,
    currency,
    "countryCode" AS CountryCode,
    kind,
    status,
    balance,
    reserved,
    version,
    id AS InternalId,
    "countryPolicyVersion" AS CountryPolicyVersion
FROM ledger."accounts"
WHERE kind = 'Customer'
AND id IN (
    SELECT
        "accountId"
    FROM ledger."entries"
    WHERE "journalId" = @InternalId)

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
WHERE "publicId" = ANY(@Ids)
ORDER BY "publicId" FOR UPDATE

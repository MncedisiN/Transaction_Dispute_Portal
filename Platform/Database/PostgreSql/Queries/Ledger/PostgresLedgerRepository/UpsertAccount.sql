INSERT INTO ledger."accounts"(
    "publicId",
    "customerPublicId",
    currency,
    "countryCode",
    kind,
    "countryPolicyVersion"
)
VALUES (
    @AccountId,
    @UserId,
    @Currency,
    @CountryCode,
    'Customer',
    @PolicyVersion
) ON CONFLICT("publicId") DO
UPDATE
SET
    "countryPolicyVersion" = EXCLUDED."countryPolicyVersion"
WHERE ledger."accounts".status = 'Active'
AND ledger."accounts"."countryPolicyVersion" <= EXCLUDED."countryPolicyVersion"

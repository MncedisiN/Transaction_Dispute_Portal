INSERT INTO ledger."accounts"(
    "publicId",
    "customerPublicId",
    currency,
    "countryCode",
    kind,
    status,
    "countryPolicyVersion"
)
VALUES (
    @AccountId,
    @UserId,
    @Currency,
    @CountryCode,
    'Customer',
    @Status,
    @PolicyVersion
) ON CONFLICT("publicId") DO NOTHING

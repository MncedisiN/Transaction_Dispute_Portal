INSERT INTO accounts."accounts"(
    "publicId",
    "customerPublicId",
    "requestId",
    "accountNumber",
    "accountType",
    "countryCode",
    currency,
    status,
    "createdAt",
    "countryPolicyVersion"
)
VALUES (
    @AccountId,
    @UserId,
    @AccountId,
    @Number,
    @AccountType,
    @CountryCode,
    @Currency,
    @Status,
    @CreatedAt,
    @PolicyVersion
)

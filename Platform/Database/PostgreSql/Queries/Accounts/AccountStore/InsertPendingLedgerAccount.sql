INSERT INTO accounts."accounts"(
    "publicId",
    "customerPublicId",
    "requestId",
    "accountNumber",
    "accountType",
    "countryCode",
    currency,
    status,
    "countryPolicyVersion"
)
VALUES (
    @Id,
    @User,
    @Request,
    @Number,
    @Type,
    @CountryCode,
    @Currency,
    'PendingLedger',
    @PolicyVersion
)

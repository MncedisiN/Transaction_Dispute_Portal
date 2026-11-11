INSERT INTO ledger."accounts"(
    "publicId",
    "customerPublicId",
    currency,
    "countryCode",
    kind
)
VALUES (
    @Id,
    @Owner,
    @Currency,
    @Country,
    'Clearing'
) ON CONFLICT DO NOTHING

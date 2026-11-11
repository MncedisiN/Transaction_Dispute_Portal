INSERT INTO ledger."aborts"(
    "publicId"
)
VALUES (
    @Id
) ON CONFLICT DO NOTHING

INSERT INTO ledger."refunds"(
    "originalTransactionPublicId",
    "refundTransactionPublicId",
    state
)
VALUES (
    @Original,
    @Current,
    'Active'
) ON CONFLICT("originalTransactionPublicId") DO
UPDATE
SET
    "refundTransactionPublicId" = EXCLUDED."refundTransactionPublicId",
    state = 'Active'

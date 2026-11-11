INSERT INTO reporting."positions"(
    "publicId",
    currency,
    balance,
    reserved,
    version
)
VALUES (
    @AccountId,
    @Currency,
    @LedgerBalance,
    @ReservedBalance,
    @Version
) ON CONFLICT("publicId") DO
UPDATE
SET
    balance = EXCLUDED.balance,
    reserved = EXCLUDED.reserved,
    version = EXCLUDED.version
WHERE reporting."positions".version < EXCLUDED.version
AND reporting."positions".currency = EXCLUDED.currency

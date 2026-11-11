UPDATE ledger."dailyUsage"
SET
    held = held + @Total
WHERE "customerPublicId" = @UserId
AND currency = @Currency
AND "usageDay" = @Day;

INSERT INTO ledger."reservations"(
    "publicId",
    "accountId",
    "customerPublicId",
    currency,
    amount,
    "usageDay",
    state,
    "expiresAt",
    reference
)
VALUES (
    @TransactionId,
    @InternalId,
    @UserId,
    @Currency,
    @Total,
    @Day,
    'Held',
    @Expires,
    @Reference
);

UPDATE ledger."accounts"
SET
    reserved = reserved + @Total,
    version = version + 1
WHERE id = @InternalId

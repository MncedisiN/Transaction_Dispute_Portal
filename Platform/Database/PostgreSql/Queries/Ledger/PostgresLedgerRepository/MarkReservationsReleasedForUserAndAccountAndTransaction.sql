UPDATE ledger."reservations"
SET
    state = 'Released'
WHERE "publicId" = @TransactionId;

UPDATE ledger."accounts"
SET
    reserved = reserved - @Amount,
    version = version + 1
WHERE id = @AccountId;

UPDATE ledger."dailyUsage"
SET
    held = held - @Amount
WHERE "customerPublicId" = @UserId
AND currency = @Currency
AND "usageDay" = @UsageDay

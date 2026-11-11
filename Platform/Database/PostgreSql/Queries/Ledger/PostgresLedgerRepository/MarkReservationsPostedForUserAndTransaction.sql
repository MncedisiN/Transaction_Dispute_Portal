UPDATE ledger."reservations"
SET
    state = 'Posted'
WHERE "publicId" = @TransactionId;

UPDATE ledger."dailyUsage"
SET
    held = held - @Amount,
    spent = spent + @Amount
WHERE "customerPublicId" = @UserId
AND currency = @Currency
AND "usageDay" = @UsageDay

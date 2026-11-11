UPDATE accounts."accounts"
SET
    "ledgerBalance" = @LedgerBalance,
    "reservedBalance" = @ReservedBalance,
    "balanceVersion" = @Version
WHERE "publicId" = @AccountId
AND "customerPublicId" = @UserId
AND currency = @Currency
AND "balanceVersion" < @Version

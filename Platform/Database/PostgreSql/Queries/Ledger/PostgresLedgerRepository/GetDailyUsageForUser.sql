SELECT
    spent,
    held
FROM ledger."dailyUsage"
WHERE "customerPublicId" = @UserId
AND currency = @Currency
AND "usageDay" = @Day FOR
UPDATE

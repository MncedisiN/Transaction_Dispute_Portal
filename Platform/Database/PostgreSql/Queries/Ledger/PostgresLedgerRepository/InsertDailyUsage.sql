INSERT INTO ledger."dailyUsage"(
    "customerPublicId",
    currency,
    "usageDay"
)
VALUES (
    @UserId,
    @Currency,
    @Day
) ON CONFLICT DO NOTHING

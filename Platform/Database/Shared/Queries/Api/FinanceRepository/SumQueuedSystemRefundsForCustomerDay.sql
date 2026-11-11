SELECT
    COALESCE(SUM("dispute_financial_commands"."amount"), 0)
FROM "disputes"."financialCommands" AS "dispute_financial_commands"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "dispute_financial_commands"."customerId") = @Customer
AND(
    SELECT
        "related2"."publicId"
    FROM "identity"."users" AS "related2"
    WHERE "related2"."id" = "dispute_financial_commands"."actorId") IS NULL
AND "dispute_financial_commands"."state" = 'Queued'
AND "dispute_financial_commands"."createdAt" >= @Day
AND "dispute_financial_commands"."createdAt" < @End
AND "dispute_financial_commands"."currencyCode" = @Currency

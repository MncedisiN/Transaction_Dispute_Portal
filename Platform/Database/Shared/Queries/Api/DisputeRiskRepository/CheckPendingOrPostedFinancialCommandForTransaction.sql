SELECT
    1
FROM "disputes"."financialCommands" financial_command
JOIN "disputes"."disputes" previous_case ON previous_case."id" = financial_command."disputeId"
WHERE previous_case."transactionId" = t."id"
AND financial_command."state" NOT IN ('Posted', 'Failed')

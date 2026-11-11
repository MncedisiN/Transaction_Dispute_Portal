SELECT
    "automatic_dispute_decisions"."outcome" AS "outcome",
    "automatic_dispute_decisions"."ruleCode" AS RuleCode,
    "automatic_dispute_decisions"."policyVersion" AS PolicyVersion
FROM "disputes"."automaticDecisions" AS "automatic_dispute_decisions"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "automatic_dispute_decisions"."disputeId") = @Id
ORDER BY "automatic_dispute_decisions"."evaluatedAt" DESC, "automatic_dispute_decisions"."id" DESC
OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY

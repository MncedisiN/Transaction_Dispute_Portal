SELECT
    "d"."publicId" AS "id"
FROM "disputes"."disputes" d
WHERE (
    SELECT
        "related1"."code"
    FROM "configuration"."disputeCategories" AS "related1"
    WHERE "related1"."id" = "d"."categoryId") IN ('Duplicate', 'DebitOrder')
AND "d"."status" = 'Submitted'
AND EXISTS (
    SELECT
        1
    FROM "transactions"."transactions" source
    WHERE "source"."id" = "d"."transactionId"
    AND "source"."archivedAt" IS NULL)
AND "d"."specialistReview" = {{FalseValue}}
AND NOT EXISTS (
    SELECT
        1
    FROM "disputes"."automaticDecisions" x
    WHERE "x"."disputeId" = "d"."id"
        AND "x"."policyVersion" = @Version)
ORDER BY "d"."createdAt", "d"."publicId"
OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY

SELECT
    COUNT(*)
FROM "disputes"."evidence" AS "dispute_evidence"
WHERE "dispute_evidence"."publicId" = @Id
AND "dispute_evidence"."deletedAt" IS NULL
AND(
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "dispute_evidence"."disputeId") = @Case

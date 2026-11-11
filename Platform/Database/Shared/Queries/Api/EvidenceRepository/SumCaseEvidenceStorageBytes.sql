SELECT
    COALESCE(SUM(CAST("dispute_evidence"."sizeBytes" AS bigint) + COALESCE(CAST("dispute_evidence"."originalSizeBytes" AS bigint), 0)), 0)
FROM "disputes"."evidence" AS "dispute_evidence"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "dispute_evidence"."disputeId") = @Id

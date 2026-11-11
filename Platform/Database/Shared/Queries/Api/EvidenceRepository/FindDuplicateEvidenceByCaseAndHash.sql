SELECT
    "dispute_evidence"."publicId" AS "id"
FROM "disputes"."evidence" AS "dispute_evidence"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "dispute_evidence"."disputeId") = @Id
AND "dispute_evidence"."deletedAt" IS NULL
AND("dispute_evidence"."sha256" = @Hash
    OR "dispute_evidence"."originalSha256" = @Hash
    OR("dispute_evidence"."sha256" = @OriginalHash
        OR "dispute_evidence"."originalSha256" = @OriginalHash))

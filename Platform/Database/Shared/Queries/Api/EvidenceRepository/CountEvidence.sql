SELECT
    COUNT(*)
FROM "disputes"."evidence" AS "dispute_evidence"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "dispute_evidence"."disputeId") = @Id

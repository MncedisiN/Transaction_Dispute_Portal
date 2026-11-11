SELECT
    COUNT(*)
FROM "disputes"."refunds" AS "dispute_refunds"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "dispute_refunds"."disputeId") = @Id

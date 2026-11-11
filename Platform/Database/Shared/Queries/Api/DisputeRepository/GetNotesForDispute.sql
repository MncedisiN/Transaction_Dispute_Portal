SELECT
    "dispute_notes"."publicId" AS "id",
    "dispute_notes"."content" AS "content",
    "dispute_notes"."visibility" AS "visibility",
    "dispute_notes"."createdAt" AS CreatedAt
FROM "disputes"."notes" AS "dispute_notes"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "dispute_notes"."disputeId") = @DisputeId {{Visibility}}
ORDER BY "createdAt" DESC, "publicId" DESC

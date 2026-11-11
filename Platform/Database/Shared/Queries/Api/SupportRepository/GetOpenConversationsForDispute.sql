SELECT
    "support_threads"."publicId" AS "id"
FROM "support"."conversations" AS "support_threads"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "support_threads"."disputeId") = @Case
AND "support_threads"."conversationStatus" = 'Open'

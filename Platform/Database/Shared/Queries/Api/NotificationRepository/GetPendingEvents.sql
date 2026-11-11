SELECT
    "e"."publicId" AS "id",
    "u"."publicId" AS UserId,
    "d"."publicId" AS DisputeId,
    "e"."kind" AS Milestone,
    'push' AS Channel,
    "e"."attempts" AS "attempts"
FROM "notifications"."events" e
JOIN "identity"."users" u ON "u"."id" = "e"."userId"
LEFT JOIN "disputes"."disputes" d ON "d"."id" = "e"."disputeId"
WHERE "e"."state" = 'Pending'
AND("e"."supportThreadId" IS NULL
    OR EXISTS (
        SELECT
            1
        FROM "support"."conversations" st
        WHERE "st"."id" = "e"."supportThreadId"
        AND {{ReadScope}}))
AND "e"."nextAttemptAt" <= @Now
AND "e"."attempts" < 5
ORDER BY "e"."createdAt"
OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY

UPDATE "notifications"."events"
SET
    "supportThreadId" = (
    SELECT
        "support_threads"."id" AS "row_id"
    FROM "support"."conversations" AS "support_threads"
    WHERE "support_threads"."publicId" = @Thread)
WHERE "events"."eventKey" = @EventId
AND "events"."userId" = @UserKey

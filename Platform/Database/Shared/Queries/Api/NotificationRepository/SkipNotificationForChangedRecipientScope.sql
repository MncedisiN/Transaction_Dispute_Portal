UPDATE "notifications"."events"
SET
    "state" = 'Skipped',
    "lastError" = 'recipient_scope_changed',
    "completedAt" = @Now
WHERE "events"."state" = 'Pending'
AND "events"."supportThreadId" IS NOT NULL
AND NOT EXISTS (
    SELECT
        1
    FROM "identity"."users" u
    JOIN "support"."conversations" st ON "st"."id" = "events"."supportThreadId"
    WHERE "u"."id" = "events"."userId"
    AND {{ReadScope}})

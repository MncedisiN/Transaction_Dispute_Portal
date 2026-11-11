SELECT
    "notification_deliveries"."publicId" AS "id",
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "notification_deliveries"."userId") AS UserId,
    (
    SELECT
        "related2"."publicId"
    FROM "disputes"."disputes" AS "related2"
    WHERE "related2"."id" = "notification_deliveries"."disputeId") AS DisputeId,
    "notification_deliveries"."milestone" AS "milestone",
    "notification_deliveries"."channel" AS "channel",
    "notification_deliveries"."attempts" AS "attempts"
FROM "notifications"."deliveries" AS "notification_deliveries"
WHERE "notification_deliveries"."state" = 'Pending'
AND "notification_deliveries"."nextAttemptAt" <= @Now
AND "notification_deliveries"."attempts" < 5
ORDER BY "notification_deliveries"."createdAt"
OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY

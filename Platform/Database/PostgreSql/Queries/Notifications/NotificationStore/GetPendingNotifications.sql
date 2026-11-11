SELECT
    n."publicId" AS "NotificationId",
    n."customerPublicId" AS "UserId",
    n."templateKey" AS "TemplateKey",
    n."aggregatePublicId" AS "AggregateId",
    n.channel,
    o.body::text AS "RequestBody"
FROM notifications."notifications" n
JOIN service_outbox o ON o.event_type = 'notification.requested'
AND o.body -> 'payload' ->> 'notificationId' = n."publicId"::text
WHERE n.status = 'Pending'
AND n.channel = 'inapp'
AND n."templateKey" LIKE 'dispute.refund.%'
ORDER BY n."createdAt", n."publicId" FOR
UPDATE OF n SKIP LOCKED
LIMIT 20

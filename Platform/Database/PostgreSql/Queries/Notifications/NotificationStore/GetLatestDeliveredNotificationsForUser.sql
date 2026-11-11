SELECT
    "publicId" AS "Id",
    "aggregatePublicId" AS "AggregateId",
    "templateKey" AS "TemplateKey",
    channel,
    status,
    "createdAt" AS "CreatedAt",
    "readAt" AS "ReadAt"
FROM notifications."notifications"
WHERE "customerPublicId" = @User
AND status = 'Delivered'
AND "templateKey" LIKE 'dispute.refund.%'
ORDER BY "createdAt" DESC, "publicId" DESC
LIMIT 100

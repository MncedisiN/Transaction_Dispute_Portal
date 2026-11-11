SELECT
    COUNT(*)
FROM "notifications"."notifications" AS "notifications"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "notifications"."userId") = @User
AND "notifications"."isRead" = {{FalseValue}}
AND ("notifications"."disputeId" IS NOT NULL
    OR "notifications"."notificationType" LIKE 'Support%'
    OR "notifications"."notificationType" LIKE 'RefundReview%')

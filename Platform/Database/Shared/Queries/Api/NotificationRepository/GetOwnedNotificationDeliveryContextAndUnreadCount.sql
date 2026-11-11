SELECT
    "e"."kind" AS "kind",
    "e"."url" AS "url",
    (
    SELECT
        CAST(COUNT(*) AS bigint)
    FROM "notifications"."notifications" AS "notifications"
    WHERE (
        SELECT
            "related1"."publicId"
        FROM "identity"."users" AS "related1"
        WHERE "related1"."id" = "notifications"."userId") = @User
    AND "notifications"."isRead" = {{FalseValue}}
    AND ("notifications"."disputeId" IS NOT NULL
    OR "notifications"."notificationType" LIKE 'Support%'
    OR "notifications"."notificationType" LIKE 'RefundReview%')) AS UnreadCount,
    "n"."body" AS Body,
    (
    SELECT
        d."publicId"
    FROM disputes.disputes d
    WHERE d.id = n."disputeId") AS DisputeId
FROM "notifications"."events" e
JOIN "identity"."users" u ON "u"."id" = "e"."userId"
JOIN "notifications"."notifications" n ON "n"."id" = "e"."notificationId"
WHERE "e"."publicId" = @Delivery
AND "u"."publicId" = @User
AND("e"."supportThreadId" IS NULL
    OR EXISTS (
        SELECT
            1
        FROM "support"."conversations" st
        WHERE "st"."id" = "e"."supportThreadId"
        AND {{ReadScope}}))

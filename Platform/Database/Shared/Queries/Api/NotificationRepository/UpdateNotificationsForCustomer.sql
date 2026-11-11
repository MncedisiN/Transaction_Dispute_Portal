UPDATE "notifications"."notifications"
SET
    "isRead" = {{TrueValue}}
WHERE "notifications"."publicId" = @NotificationId
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "notifications"."userId") = @CustomerId
AND ("notifications"."disputeId" IS NOT NULL
    OR "notifications"."notificationType" LIKE 'Support%'
    OR "notifications"."notificationType" LIKE 'RefundReview%')

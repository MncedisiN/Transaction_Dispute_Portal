("notifications"."disputeId" IS NOT NULL
    OR "notifications"."notificationType" LIKE 'Support%'
    OR "notifications"."notificationType" LIKE 'RefundReview%')

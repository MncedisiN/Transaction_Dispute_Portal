INSERT INTO "notifications"."events"(
    "publicId",
    "eventKey",
    "userId",
    "notificationId",
    "disputeId",
    "kind",
    "url",
    "state",
    "attempts",
    "nextAttemptAt",
    "createdAt"
)
SELECT
    @Id,
    @EventId,
    @UserKey,
    "n"."id",
    "d"."id",
    @Kind,
    @Url,
    'Pending',
    0,
    @Now,
    @Now
FROM "notifications"."notifications" n
LEFT JOIN "disputes"."disputes" d ON "d"."publicId" = @Dispute
WHERE "n"."publicId" = @Notification

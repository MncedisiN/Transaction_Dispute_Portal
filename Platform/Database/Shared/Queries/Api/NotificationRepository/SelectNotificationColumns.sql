SELECT
    "notifications"."publicId" AS Id,
    (
    SELECT
        dispute."publicId"
    FROM "disputes"."disputes" AS dispute
    WHERE dispute."id" = "notifications"."disputeId") AS DisputeId,
    "notifications"."notificationType" AS Type,
    "notifications"."title" AS Title,
    "notifications"."body" AS Body,
    "notifications"."isRead" AS IsRead,
    "notifications"."createdAt" AS CreatedAt,
    (
    SELECT
        "e"."url" AS "url"
    FROM "notifications"."events" e
    WHERE "e"."notificationId" = "notifications"."id") AS Url

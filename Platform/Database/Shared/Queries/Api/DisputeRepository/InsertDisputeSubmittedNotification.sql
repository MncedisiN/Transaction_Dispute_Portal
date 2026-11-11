INSERT INTO "notifications"."notifications"(
    "publicId",
    "userId",
    "disputeId",
    "notificationType",
    "title",
    "body"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "identity"."users" AS "resolved1"
        WHERE "resolved1"."publicId" = @CustomerId),
    (
        SELECT
            "resolved2"."id"
        FROM "disputes"."disputes" AS "resolved2"
        WHERE "resolved2"."publicId" = @DisputeId),
    'DisputeCreated',
    'Dispute received',
    'Your dispute has been received and is ready for review.'
)

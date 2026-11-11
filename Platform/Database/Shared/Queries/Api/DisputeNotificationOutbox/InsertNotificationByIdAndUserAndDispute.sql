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
        WHERE "resolved1"."publicId" = @User),
    (
        SELECT
            "resolved2"."id"
        FROM "disputes"."disputes" AS "resolved2"
        WHERE "resolved2"."publicId" = @Dispute),
    'NewCase',
    'New dispute ready for review',
    'Review the case queue to see assignment and service targets.'
)

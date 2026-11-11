INSERT INTO "disputes"."statusHistory"(
    "publicId",
    "disputeId",
    "previousStatus",
    "newStatus",
    "actorUserId",
    "changedAt",
    "comment"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "disputes"."disputes" AS "resolved1"
        WHERE "resolved1"."publicId" = @Case),
    'Submitted',
    'UnderReview',
    (
        SELECT
            "resolved2"."id"
        FROM "identity"."users" AS "resolved2"
        WHERE "resolved2"."publicId" = @Actor),
    @Now,
    @Reason
)

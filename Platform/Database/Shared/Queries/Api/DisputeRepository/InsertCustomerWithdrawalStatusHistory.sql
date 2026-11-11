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
        WHERE "resolved1"."publicId" = @Dispute),
    @Previous,
    'Withdrawn',
    (
        SELECT
            "resolved2"."id"
        FROM "identity"."users" AS "resolved2"
        WHERE "resolved2"."publicId" = @Customer),
    @Now,
    @Explanation
);

INSERT INTO "disputes"."notes"(
    "publicId",
    "disputeId",
    "authorUserId",
    "content",
    "visibility",
    "createdAt"
)
VALUES (
    @Note,
    (
        SELECT
            "resolved3"."id"
        FROM "disputes"."disputes" AS "resolved3"
        WHERE "resolved3"."publicId" = @Dispute),
    (
        SELECT
            "resolved4"."id"
        FROM "identity"."users" AS "resolved4"
        WHERE "resolved4"."publicId" = @Customer),
    @Explanation,
    'Customer',
    @Now
)

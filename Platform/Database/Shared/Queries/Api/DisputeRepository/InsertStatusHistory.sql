INSERT INTO "disputes"."statusHistory"(
    "publicId",
    "disputeId",
    "newStatus",
    "actorUserId",
    "changedAt",
    "comment"
)
VALUES (
    @History,
    (
        SELECT
            "resolved1"."id"
        FROM "disputes"."disputes" AS "resolved1"
        WHERE "resolved1"."publicId" = @Child),
    'Submitted',
    (
        SELECT
            "resolved2"."id"
        FROM "identity"."users" AS "resolved2"
        WHERE "resolved2"."publicId" = @Customer),
    @Now,
    @Comment
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
        WHERE "resolved3"."publicId" = @Parent),
    (
        SELECT
            "resolved4"."id"
        FROM "identity"."users" AS "resolved4"
        WHERE "resolved4"."publicId" = @Customer),
    @Comment,
    'Customer',
    @Now
)

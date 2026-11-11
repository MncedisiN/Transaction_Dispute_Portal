INSERT INTO "disputes"."statusHistory"(
    "publicId",
    "disputeId",
    "newStatus",
    "actorUserId",
    "comment"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "disputes"."disputes" AS "resolved1"
        WHERE "resolved1"."publicId" = @DisputeId),
    'Submitted',
    (
        SELECT
            "resolved2"."id"
        FROM "identity"."users" AS "resolved2"
        WHERE "resolved2"."publicId" = @Actor),
    @Comment
)

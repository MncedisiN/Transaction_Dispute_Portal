INSERT INTO "disputes"."statusHistory"(
    "publicId",
    "disputeId",
    "previousStatus",
    "newStatus",
    "actorUserId",
    "comment"
)
VALUES (
    @HistoryId,
    (
        SELECT
            "resolved1"."id"
        FROM "disputes"."disputes" AS "resolved1"
        WHERE "resolved1"."publicId" = @DisputeId),
    @Previous,
    @Next,
    (
        SELECT
            "resolved2"."id"
        FROM "identity"."users" AS "resolved2"
        WHERE "resolved2"."publicId" = @ActorId),
    @Comment
)

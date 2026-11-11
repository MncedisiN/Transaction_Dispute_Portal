INSERT INTO "disputes"."notes"(
    "publicId",
    "disputeId",
    "authorUserId",
    "content",
    "visibility"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "disputes"."disputes" AS "resolved1"
        WHERE "resolved1"."publicId" = @Case),
    (
        SELECT
            "resolved2"."id"
        FROM "identity"."users" AS "resolved2"
        WHERE "resolved2"."publicId" = @Actor),
    'Supporting evidence attached.',
    'Customer'
)

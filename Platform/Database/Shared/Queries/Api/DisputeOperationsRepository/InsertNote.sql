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
        WHERE "resolved1"."publicId" = @Dispute),
    (
        SELECT
            "resolved2"."id"
        FROM "identity"."users" AS "resolved2"
        WHERE "resolved2"."publicId" = @Actor),
    'Your dispute has been escalated for senior review. The team will review the next steps and keep the outcome here.',
    'Customer'
)

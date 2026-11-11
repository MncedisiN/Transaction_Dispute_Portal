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
    @History,
    (
        SELECT
            "resolved1"."id"
        FROM "disputes"."disputes" AS "resolved1"
        WHERE "resolved1"."publicId" = @Id),
    'Submitted',
    'UnderReview',
    NULL,
    @Now,
    @Reason
);

UPDATE "disputes"."disputes"
SET
    "status" = 'UnderReview',
    "firstResponseAt" = COALESCE("disputes"."firstResponseAt", @Now),
    "updatedAt" = @Now,
    "version" = "disputes"."version" + 1
WHERE "disputes"."publicId" = @Id
AND "disputes"."version" = @Version

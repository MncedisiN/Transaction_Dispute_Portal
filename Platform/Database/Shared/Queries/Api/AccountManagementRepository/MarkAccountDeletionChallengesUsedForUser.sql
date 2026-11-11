UPDATE "identity"."accountDeletionChallenges"
SET
    "state" = 'Used'
WHERE "accountDeletionChallenges"."publicId" = @Challenge;

INSERT INTO "identity"."accountDeletionRequests"(
    "publicId",
    "userId",
    "reference",
    "status",
    "protectedReason",
    "createdAt"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "identity"."users" AS "resolved1"
        WHERE "resolved1"."publicId" = @User),
    @Reference,
    'Pending',
    @Reason,
    @Now
)

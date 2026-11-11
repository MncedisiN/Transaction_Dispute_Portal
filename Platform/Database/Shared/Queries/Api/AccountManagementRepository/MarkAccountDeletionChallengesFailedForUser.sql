UPDATE "identity"."accountDeletionChallenges"
SET
    "state" = 'Failed'
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "accountDeletionChallenges"."userId") = @User
AND "accountDeletionChallenges"."state" IN ('Pending', 'Ready');

INSERT INTO "identity"."accountDeletionChallenges"(
    "publicId",
    "userId",
    "codeHash",
    "state",
    "createdAt",
    "expiresAt"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved2"."id"
        FROM "identity"."users" AS "resolved2"
        WHERE "resolved2"."publicId" = @User),
    @Hash,
    'Pending',
    @Now,
    @Expires
)

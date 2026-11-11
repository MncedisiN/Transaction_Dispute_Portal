UPDATE "identity"."accountDeletionChallenges"
SET
    "state" = @State
WHERE "accountDeletionChallenges"."publicId" = @Id
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "accountDeletionChallenges"."userId") = @User
AND "accountDeletionChallenges"."state" = 'Pending'

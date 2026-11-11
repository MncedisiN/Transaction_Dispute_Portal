UPDATE "identity"."contactVerificationChallenges"
SET
    "state" = @State
WHERE "contactVerificationChallenges"."publicId" = @Id
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "contactVerificationChallenges"."userId") = @User
AND "contactVerificationChallenges"."state" = 'Pending'

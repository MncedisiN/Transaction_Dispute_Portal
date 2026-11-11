UPDATE "identity"."identityChallenges"
SET
    "state" = 'Failed',
    "protectedPayload" = ''
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "identityChallenges"."userId") = @User
AND "identityChallenges"."state" IN ('Ready', 'Pending');

UPDATE "identity"."contactVerificationChallenges"
SET
    "state" = 'Used'
WHERE "contactVerificationChallenges"."publicId" = @Id;

UPDATE "identity"."userSessions"
SET
    "revokedAt" = @Now
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "userSessions"."userId") = @User
AND "userSessions"."revokedAt" IS NULL;

UPDATE "identity"."accountDeletionChallenges"
SET
    "state" = 'Failed'
WHERE (
    SELECT
        "related2"."publicId"
    FROM "identity"."users" AS "related2"
    WHERE "related2"."id" = "accountDeletionChallenges"."userId") = @User
AND "accountDeletionChallenges"."state" IN ('Ready', 'Pending')

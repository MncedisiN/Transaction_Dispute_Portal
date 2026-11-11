UPDATE "identity"."userSessions"
SET
    "revokedAt" = @Now
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "userSessions"."userId") = @Id
AND "userSessions"."revokedAt" IS NULL;

UPDATE "identity"."identityChallenges"
SET
    "state" = 'Failed',
    "protectedPayload" = ''
WHERE (
    SELECT
        "related2"."publicId"
    FROM "identity"."users" AS "related2"
    WHERE "related2"."id" = "identityChallenges"."userId") = @Id
AND "identityChallenges"."state" IN ('Pending', 'Ready');

UPDATE "identity"."contactVerificationChallenges"
SET
    "state" = 'Failed'
WHERE (
    SELECT
        "related3"."publicId"
    FROM "identity"."users" AS "related3"
    WHERE "related3"."id" = "contactVerificationChallenges"."userId") = @Id
AND "contactVerificationChallenges"."state" IN ('Pending', 'Ready');

UPDATE "identity"."accountDeletionChallenges"
SET
    "state" = 'Failed'
WHERE (
    SELECT
        "related4"."publicId"
    FROM "identity"."users" AS "related4"
    WHERE "related4"."id" = "accountDeletionChallenges"."userId") = @Id
AND "accountDeletionChallenges"."state" IN ('Pending', 'Ready')

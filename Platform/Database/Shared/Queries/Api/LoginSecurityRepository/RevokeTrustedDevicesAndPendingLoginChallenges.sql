UPDATE "identity"."trustedLoginDevices"
SET
    "revokedAt" = @Now
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "trustedLoginDevices"."userId") = @User
AND "trustedLoginDevices"."revokedAt" IS NULL;

UPDATE "identity"."identityChallenges"
SET
    "state" = 'Failed',
    "protectedPayload" = ''
WHERE (
    SELECT
        "related2"."publicId"
    FROM "identity"."users" AS "related2"
    WHERE "related2"."id" = "identityChallenges"."userId") = @User
AND "identityChallenges"."purpose" = 'Login'
AND "identityChallenges"."state" IN ('Pending', 'Ready')

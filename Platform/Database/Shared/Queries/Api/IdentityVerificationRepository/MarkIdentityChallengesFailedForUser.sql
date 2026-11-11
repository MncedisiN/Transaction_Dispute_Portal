UPDATE "identity"."identityChallenges"
SET
    "state" = 'Failed',
    "protectedPayload" = ''
WHERE "identityChallenges"."destinationHash" = @Hash
AND "identityChallenges"."purpose" = @Purpose
AND "identityChallenges"."state" IN ('Pending', 'Ready');

INSERT INTO "identity"."identityChallenges"(
    "publicId",
    "userId",
    "purpose",
    "channel",
    "destinationHash",
    "protectedPayload",
    "codeHash",
    "state",
    "createdAt",
    "expiresAt"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "identity"."users" AS "resolved1"
        WHERE "resolved1"."publicId" = @User),
    @Purpose,
    @Channel,
    @Hash,
    @Payload,
    @CodeHash,
    'Pending',
    @Now,
    @Expires
)

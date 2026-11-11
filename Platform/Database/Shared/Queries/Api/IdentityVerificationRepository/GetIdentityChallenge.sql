SELECT
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "identity_challenges"."userId") AS UserId,
    "identity_challenges"."channel" AS "channel",
    "identity_challenges"."protectedPayload" AS ProtectedPayload,
    "identity_challenges"."codeHash" AS CodeHash,
    "identity_challenges"."state" AS "state",
    "identity_challenges"."attempts" AS "attempts",
    "identity_challenges"."expiresAt" AS ExpiresAt
FROM "identity"."identityChallenges" AS "identity_challenges"
WHERE "identity_challenges"."publicId" = @Id
AND "identity_challenges"."purpose" = @Purpose

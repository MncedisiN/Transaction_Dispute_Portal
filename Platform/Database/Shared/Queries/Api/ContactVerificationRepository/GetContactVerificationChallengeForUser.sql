SELECT
    challenge.purpose,
    challenge.channel,
    challenge."protectedDestination" AS ProtectedDestination,
    challenge."codeHash" AS CodeHash,
    challenge.state,
    challenge.attempts,
    challenge."expiresAt" AS ExpiresAt
FROM "identity"."contactVerificationChallenges" challenge
WHERE challenge."publicId" = @Id
AND challenge."userId" = (
    SELECT
        id
    FROM "identity"."users"
    WHERE "publicId" = @User)
